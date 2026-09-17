from pathlib import Path
import os

from mythic_container.C2ProfileBase import *


class Telegram(C2Profile):
    name = "telegram"
    description = "Telegram Bot API"
    author = "@DavidCarliez"
    is_p2p = False
    is_server_routed = False
    server_binary_path = Path(os.path.join(".", "telegram", "c2_code", "telegram"))
    server_folder_path = Path(os.path.join(".", "telegram", "c2_code"))
    parameters = [
        C2ProfileParameter(
            name="bot_token",
            description="Dedicated Bot API token for this payload",
            default_value="",
            verifier_regex=r"^[0-9]+:[A-Za-z0-9_-]+$",
            required=True,
        ),
        C2ProfileParameter(
            name="controller_bot",
            description="Controller bot username, with or without the leading @",
            default_value="",
            verifier_regex=r"^@?[A-Za-z][A-Za-z0-9_]{4,31}$",
            required=True,
        ),
        C2ProfileParameter(
            name="api_base",
            description="Telegram Bot API base URL",
            default_value="https://api.telegram.org",
            verifier_regex=r"^https?://.+",
            required=True,
        ),
        C2ProfileParameter(
            name="message_checks",
            description="Maximum long-poll attempts while waiting for each response",
            default_value="10",
            verifier_regex=r"^[1-9][0-9]*$",
            required=True,
        ),
        C2ProfileParameter(
            name="time_between_checks",
            description="Telegram long-poll timeout in seconds",
            default_value="10",
            verifier_regex=r"^[1-9][0-9]*$",
            required=True,
        ),
        C2ProfileParameter(
            name="callback_interval",
            description="Callback interval in seconds",
            default_value="60",
            verifier_regex=r"^[1-9][0-9]*$",
            required=True,
        ),
        C2ProfileParameter(
            name="callback_jitter",
            description="Callback jitter percentage",
            default_value="10",
            verifier_regex=r"^[0-9]+$",
            required=True,
        ),
        C2ProfileParameter(
            name="encrypted_exchange_check",
            description="Perform key exchange",
            choices=["T", "F"],
            parameter_type=ParameterType.ChooseOne,
            default_value="T",
            required=False,
        ),
        C2ProfileParameter(
            name="AESPSK",
            description="Message encryption",
            default_value="aes256_hmac",
            parameter_type=ParameterType.ChooseOne,
            choices=["aes256_hmac"],
            required=True,
            crypto_type=True,
        ),
        C2ProfileParameter(
            name="user_agent",
            description="HTTP User-Agent",
            default_value="Mozilla/5.0",
            required=False,
        ),
        C2ProfileParameter(
            name="proxy_host",
            description="Proxy URL",
            default_value="",
            required=False,
            verifier_regex=r"^$|^https?://.+",
        ),
        C2ProfileParameter(
            name="proxy_port",
            description="Proxy port",
            default_value="",
            verifier_regex=r"^$|^[0-9]+$",
            required=False,
        ),
        C2ProfileParameter(
            name="proxy_user",
            description="Proxy username",
            default_value="",
            required=False,
        ),
        C2ProfileParameter(
            name="proxy_pass",
            description="Proxy password",
            default_value="",
            required=False,
        ),
        C2ProfileParameter(
            name="killdate",
            description="Kill date",
            parameter_type=ParameterType.Date,
            default_value=365,
            required=False,
        ),
    ]
