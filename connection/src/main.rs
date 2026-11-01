use anyhow::{Context, Result, bail};
use base64::{Engine, engine::general_purpose::URL_SAFE_NO_PAD};
use iroh::{Endpoint, SecretKey, EndpointAddr, endpoint::presets};
use serde_json::{Value, json};
use std::{env, fs, path::PathBuf, sync::Arc, time::Duration};
use tokio::sync::Mutex;

const ALPN: &[u8] = b"gamedailyagent/connection/1";
const LIMIT: usize = 4 * 1024 * 1024;

struct State {
    root: PathBuf,
    token: String,
    devices: Mutex<Vec<String>>,
    http: reqwest::Client,
    url: String,
    api_key: String,
}

fn response(id: &Value, status: u16, body: Value) -> Value {
    json!({"protocolVersion":1,"messageId":id,"statusCode":status,"body":body.to_string()})
}

async fn request(state: &State, remote: &str, input: Value) -> Value {
    let id = &input["messageId"];
    if input["protocolVersion"] != 1 {
        return response(id, 400, json!({"detail":"Unsupported connection protocol"}));
    }
    if let Some(token) = input["pairingToken"].as_str() {
        if token != state.token { return response(id, 401, json!({"detail":"连接码不正确，请扫描电脑上的连接码。"})); }
        let mut devices = state.devices.lock().await;
        if !devices.iter().any(|d| d == remote) {
            devices.push(remote.to_owned());
            if let Err(error) = fs::write(state.root.join("devices.json"), serde_json::to_vec(&*devices).unwrap()) {
                devices.pop();
                return response(id, 500, json!({"detail":error.to_string()}));
            }
        }
        return response(id, 200, json!({"paired":true}));
    }
    if !state.devices.lock().await.iter().any(|d| d == remote) {
        return response(id, 401, json!({"detail":"请先扫描电脑上的连接码。"}));
    }
    if input["frameType"] == "transportProbe" { return response(id, 200, json!({"alive":true})); }
    let result: Result<(u16, String)> = async {
        let method = input["method"].as_str().context("Missing method")?.parse::<reqwest::Method>()?;
        let path = input["path"].as_str().context("Missing path")?;
        if !path.starts_with('/') || path.starts_with("//") || path.contains("://") { bail!("Invalid path"); }
        let mut req = state.http.request(method, format!("{}{path}", state.url))
            .header("X-Gda-Key", &state.api_key).header("Content-Type", "application/json");
        if let Some(body) = input["body"].as_str() { req = req.body(body.to_owned()); }
        let result = req.send().await?;
        Ok((result.status().as_u16(), result.text().await?))
    }.await;
    match result {
        Ok((status, body)) => json!({"protocolVersion":1,"messageId":id,"statusCode":status,"body":body}),
        Err(error) => response(id, 502, json!({"detail":error.to_string()})),
    }
}

async fn serve(state: Arc<State>, conn: iroh::endpoint::Connection) -> Result<()> {
    let remote = conn.remote_id().to_string();
    while let Ok((mut send, mut receive)) = conn.accept_bi().await {
        let state = state.clone();
        let remote = remote.clone();
        tokio::spawn(async move {
            let result: Result<()> = async {
                let input: Value = serde_json::from_slice(&tokio::time::timeout(Duration::from_secs(30), receive.read_to_end(LIMIT)).await??)?;
                let bytes = serde_json::to_vec(&request(&state, &remote, input).await)?;
                send.write_all(&bytes).await?;
                send.finish()?;
                Ok(())
            }.await;
            if let Err(error) = result { eprintln!("phone stream: {error}"); }
        });
    }
    Ok(())
}

// Uses exactly the Android wire format, for transport acceptance without changing the app.
async fn client(args: &[String]) -> Result<()> {
    let code: Value = serde_json::from_slice(&fs::read(&args[2])?)?;
    let endpoint = Endpoint::builder(presets::N0).bind().await?;
    let addr: EndpointAddr = serde_json::from_slice(&URL_SAFE_NO_PAD.decode(code["pcEndpointAddr"].as_str().context("address")?)?)?;
    let conn = endpoint.connect(addr, ALPN).await?;
    let pair = json!({"protocolVersion":1,"messageId":"pair-test","pairingToken":code["pairingToken"],"displayName":"Protocol acceptance"});
    let (mut send, mut recv) = conn.open_bi().await?;
    send.write_all(&serde_json::to_vec(&pair)?).await?; send.finish()?;
    let reply: Value = serde_json::from_slice(&recv.read_to_end(LIMIT).await?)?;
    if reply["statusCode"] != 200 { bail!("Pairing failed: {reply}"); }
    let body = args.get(5).cloned();
    let frame = json!({"protocolVersion":1,"messageId":"request-test","method":args.get(3).map(String::as_str).unwrap_or("GET"),"path":args.get(4).map(String::as_str).unwrap_or("/profile"),"body":body});
    let (mut send, mut recv) = conn.open_bi().await?;
    send.write_all(&serde_json::to_vec(&frame)?).await?; send.finish()?;
    println!("{}", String::from_utf8(recv.read_to_end(LIMIT).await?)?);
    endpoint.close().await;
    Ok(())
}

#[tokio::main]
async fn main() -> Result<()> {
    let args: Vec<_> = env::args().collect();
    if args.get(1).map(String::as_str) == Some("client") { return client(&args).await; }
    // The desktop owns this process. Its closed pipe also covers a desktop crash.
    std::thread::spawn(|| {
        let mut byte = [0u8; 1];
        while std::io::Read::read(&mut std::io::stdin(), &mut byte).unwrap_or(0) > 0 {}
        std::process::exit(0);
    });
    let root = PathBuf::from(env::var("GDA_DATA")?);
    fs::create_dir_all(&root)?;
    let key_path = root.join("phone-secret");
    let secret = if key_path.exists() {
        let bytes: [u8; 32] = URL_SAFE_NO_PAD.decode(fs::read_to_string(&key_path)?.trim())?.try_into().map_err(|_| anyhow::anyhow!("Invalid phone identity"))?;
        SecretKey::from_bytes(&bytes)
    } else {
        let secret = SecretKey::generate();
        fs::write(&key_path, URL_SAFE_NO_PAD.encode(secret.to_bytes()))?;
        secret
    };
    let token_path = root.join("pairing-token");
    let token = if token_path.exists() { fs::read_to_string(&token_path)? } else {
        let token = URL_SAFE_NO_PAD.encode(SecretKey::generate().to_bytes()); fs::write(&token_path, &token)?; token
    };
    let endpoint = Endpoint::builder(presets::N0).secret_key(secret).alpns(vec![ALPN.to_vec()]).bind().await?;
    // Publishing a code must not prevent the local desktop from starting while offline.
    let _ = tokio::time::timeout(Duration::from_secs(15), endpoint.online()).await;
    let code = json!({"v":1,"pcEndpointId":endpoint.id().to_string(),"pcEndpointAddr":URL_SAFE_NO_PAD.encode(serde_json::to_vec(&endpoint.addr())?),"pcName":format!("GDA-ZZZ · {}",env::var("COMPUTERNAME").unwrap_or_default()),"pairingToken":token,"expiresAt":4102444800000u64});
    fs::write(root.join("pairing.json"), serde_json::to_vec_pretty(&code)?)?;
    println!("READY");
    let devices = if root.join("devices.json").exists() { serde_json::from_slice(&fs::read(root.join("devices.json"))?)? } else { Vec::new() };
    let state = Arc::new(State {root, token, devices: Mutex::new(devices), http:reqwest::Client::builder().timeout(Duration::from_secs(240)).build()?,url:env::var("GDA_URL")?,api_key:env::var("GDA_KEY")?});
    while let Some(incoming) = endpoint.accept().await {
        let state = state.clone();
        tokio::spawn(async move { if let Ok(conn) = incoming.await { let _ = serve(state, conn).await; } });
    }
    Ok(())
}
