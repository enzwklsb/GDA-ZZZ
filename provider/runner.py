"""The only game execution entry. Uses the official OneDragon Python API."""
import json
import os
from pathlib import Path
import sys
import threading
import time
import traceback


def emit(kind, value):
    print(f"GDA_{kind}={value}", flush=True)


def prepare(root):
    os.chdir(root)
    sys.path.insert(0, str(root / "src"))
    from one_dragon.utils import os_utils
    os_utils.set_work_dir(root)


def run(root, request):
    from PySide6.QtWidgets import QApplication
    from zzz_od.context.zzz_context import ZContext
    app = QApplication.instance() or QApplication([])
    ctx = ZContext()
    ctx.game_account_config.game_path = request["gamePath"]
    ctx.game_account_config.platform = "PC"
    # Use the OCR models shipped in the official full environment, with no extra gate.
    ctx.init()
    if not ctx.ready_for_application:
        raise RuntimeError("OneDragon 初始化失败，请查看前面的具体错误。")
    stopped = threading.Event()

    def watch_stop():
        while not stopped.wait(0.2):
            if Path(request["stopFile"]).exists():
                ctx.run_context.stop_running()
                return

    threading.Thread(target=watch_stop, daemon=True).start()
    try:
        for task_id in request["taskIds"]:
            if Path(request["stopFile"]).exists():
                raise InterruptedError("任务已停止。")
            emit("STEP", task_id)
            result = ctx.run_context.run_application(task_id, ctx.current_instance_idx, "gda")
            if str(result.finish_reason) != "COMPLETED":
                detail = ctx.run_context.last_application_result
                raise RuntimeError(f"{task_id}：{getattr(detail, 'status', None) or result.finish_reason}")
        emit("RESULT", "succeeded")
    finally:
        stopped.set()
        ctx.run_context.stop_running()
        if ctx.controller is not None:
            ctx.controller.cleanup_after_app_shutdown()
        ctx.btn_listener.stop()
        app.processEvents()


def main():
    command, raw_root = sys.argv[1:3]
    root = Path(raw_root).resolve()
    prepare(root)
    if command == "check":
        from zzz_od.context.zzz_context import ZContext
        from one_dragon.base.operation.application.application_run_context import ApplicationRunContext
        import cv2
        import onnxruntime
        import win32gui
        print("OneDragon、Python、OCR 和 Windows 控制依赖已就绪。", flush=True)
    elif command == "run":
        run(root, json.loads(Path(sys.argv[3]).read_text(encoding="utf-8-sig")))
    else:
        raise ValueError(f"Unknown command: {command}")


if __name__ == "__main__":
    code = 0
    try:
        main()
    except BaseException as error:
        traceback.print_exc()
        emit("ERROR", str(error))
        code = 1
    sys.stdout.flush()
    sys.stderr.flush()
    # OneDragon owns worker pools/listeners. The task process ends with the task.
    os._exit(code)
