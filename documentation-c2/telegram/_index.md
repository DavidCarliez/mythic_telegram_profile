+++
title = "telegram"
chapter = false
weight = 5
+++

## Overview

The Telegram C2 profile connects Athena agents to Mythic through private messages between Telegram bots.

```text
Athena agent bot <-> Telegram Bot API <-> controller bot <-> C2 service <-> Mythic
```

The controller service uses Mythic's Push C2 gRPC interface. It does not decrypt Athena messages.

## Telegram setup

1. Create one controller bot with [@BotFather](https://t.me/BotFather).
2. Create a dedicated bot for each concurrently running Athena payload.
3. Enable **Bot-to-Bot Communication Mode** for the controller and agent bots.
4. Keep the bot tokens private.

Private bot-to-bot messages require the communication mode on both participants. Do not reuse an agent bot token for concurrent payloads because all `getUpdates` consumers for that token share one queue.

## Controller configuration

Open **C2 Profiles**, expand `telegram`, and select **View/Edit Config**.

| Key | Description |
| --- | --- |
| `botToken` | Controller bot token |
| `apiBase` | Bot API base URL; normally `https://api.telegram.org` |
| `pollTimeout` | Long-poll timeout from 1 through 50 seconds |
| `mythicGrpc` | Mythic Push C2 gRPC endpoint |

Save the configuration and start the profile before using a payload.

## Payload configuration

| Parameter | Description |
| --- | --- |
| `bot_token` | Dedicated token for this payload's bot |
| `controller_bot` | Controller bot username |
| `api_base` | Bot API base URL |
| `message_checks` | Long polls attempted while waiting for a response |
| `time_between_checks` | Long-poll timeout in seconds |
| `callback_interval` | Callback interval in seconds |
| `callback_jitter` | Callback jitter percentage |
| `AESPSK` | Athena message encryption; use `aes256_hmac` |
| `user_agent` | HTTP User-Agent |
| `proxy_host`, `proxy_port`, `proxy_user`, `proxy_pass` | Optional HTTP proxy settings |
| `killdate` | Payload expiration date |

## Transport behavior

Messages are JSON envelopes split into 2,800-character chunks. A chunk set is limited to 256 chunks and expires after ten minutes. The service associates each random agent route identifier with the private chat that delivered it, then sends Mythic responses back to that chat.

The agent retains each outbound request until it receives a correlated response. If Telegram response delivery fails, the controller caches and replays the Mythic response without forwarding the request twice.
Mythic can push tasking while no agent request is pending. The controller queues that tasking and delivers it with the next agent exchange instead of dropping it.


The controller reports an agent route as disconnected after three missed maximum-jitter callback intervals plus 30 seconds, with a minimum timeout of 60 seconds. This closes Mythic's streaming edge and replaces the streaming timestamp with the disconnect time.

## Security considerations

- Bot tokens are bearer credentials. Revoke them through BotFather when an operation ends.
- Agent tokens are embedded in payloads.
- Telegram messages are not end-to-end encrypted. Keep Athena's `AESPSK` encryption enabled.
- Telegram observes transport metadata and may retain content according to its service policies.
- The profile uses long polling rather than webhooks.
