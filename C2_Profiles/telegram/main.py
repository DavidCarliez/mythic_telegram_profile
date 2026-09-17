import subprocess

import mythic_container

from telegram.c2_functions.telegram import *


result = subprocess.run(
    ["dotnet", "publish", "-c", "Release", "-o", "/Mythic/telegram/c2_code/"],
    cwd="/Mythic/telegram/c2_code/src/telegram",
    check=False,
)
if result.returncode != 0:
    raise RuntimeError("failed to build the Telegram C2 service")

mythic_container.mythic_service.start_and_run_forever()
