"""Private, send-only SMTP notification for a recognized usage limit."""
from email.message import EmailMessage
import json
import os
from pathlib import Path
import re
import smtplib
import ssl
import stat

TEXT = ("A ChatGPT plan or app-specific usage limit blocked a request.\n"
        "Check ChatGPT Settings > Usage before retrying plan requests.\n"
        "The remaining percentage and reset time are unknown.\n")

def configuration(path):
    path = Path(path).absolute()
    if any((parent / ".git").exists() for parent in path.resolve().parents):
        raise ValueError("credentials must be outside repositories")
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd) as source:
        info = os.fstat(source.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
            raise ValueError("credentials must be owner-only")
        config = json.load(source)
    if not isinstance(config, dict) or set(config) != {"host", "port", "username", "password", "sender", "recipient", "recipient_confirmed"} or config["recipient_confirmed"] is not True:
        raise ValueError("explicit recipient confirmation required")
    if not isinstance(config["host"], str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]{0,252}", config["host"]):
        raise ValueError("invalid SMTP host")
    if type(config["port"]) is not int or not 1 <= config["port"] <= 65535:
        raise ValueError("invalid SMTP port")
    for field in ("sender", "recipient"):
        if not isinstance(config[field], str) or not re.fullmatch(r"[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9.-]+", config[field]):
            raise ValueError("invalid mailbox")
    for field in ("username", "password"):
        if not isinstance(config[field], str) or not config[field] or len(config[field]) > 4096 or "\n" in config[field] or "\r" in config[field]:
            raise ValueError("invalid credential")
    return config


def send(config, text):
    message = EmailMessage()
    message["From"] = config["sender"]
    message["To"] = config["recipient"]
    message["Subject"] = "ChatGPT usage limit reached"
    message.set_content(text)
    with smtplib.SMTP_SSL(config["host"], config["port"], timeout=10, context=ssl.create_default_context()) as smtp:
        smtp.login(config["username"], config["password"])
        if smtp.send_message(message, from_addr=config["sender"], to_addrs=[config["recipient"]]):
            raise ValueError("recipient not accepted")


def deliver(config_path, incident, alerts, persist, transport=None):
    if not config_path:
        return "unconfigured"
    config = configuration(config_path)
    if incident in alerts:
        return alerts[incident]
    alerts[incident] = "send_uncertain"
    persist()
    try:
        (transport or send)(config, TEXT)
    except Exception:
        return "send_uncertain"
    alerts[incident] = "delivered"
    persist()
    return "delivered"
