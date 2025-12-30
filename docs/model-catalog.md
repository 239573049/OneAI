# 模型清单与映射指南

## 目的
用于 OneAI 的模型命名与映射参考。此文档仅基于当前 Antigravity 账户可见模型列表与本项目映射机制，不代表官方能力说明。

## 适用范围
- Claude Code（Anthropic /v1/messages）
- Codex CLI（OpenAI /v1/chat/completions，wire_api=chat）
- OneAI 后端的模型映射配置：system_settings.model_mapping_rules

## Antigravity 可见模型清单（你提供的列表）
| 模型名 | 类型/用途备注（按命名语义） |
| --- | --- |
| rev19-uic3-1p | 内部/未知模型，建议避免作为默认 |
| gemini-3-pro-image | 可能偏图像能力，需实测确认 |
| gemini-2.5-flash | 速度优先，轻量 |
| gemini-3-flash | 速度优先，轻量 |
| claude-sonnet-4-5 | Claude 风格，均衡 |
| gemini-2.5-flash-thinking | 带思考的 flash 版本 |
| gemini-3-pro-low | 轻量/低档版本 |
| gemini-2.5-pro | 质量优先/均衡旗舰 |
| gemini-3-pro-high | 质量优先/高档版本 |
| chat_23310 | 内部/未知模型，建议避免作为默认 |
| chat_20706 | 内部/未知模型，建议避免作为默认 |
| claude-opus-4-5-thinking | Claude 风格，深度推理 |
| gpt-oss-120b-medium | 开源模型风格，需实测能力 |
| claude-sonnet-4-5-thinking | Claude 风格，中强推理 |
| gemini-2.5-flash-lite | 更轻量版本 |

## 建议的对外“可填”模型名
用于 Claude Code / Codex CLI 侧填写，实际通过映射落到 Antigravity 模型。

### Claude Code（Anthropic /v1/messages）
- claude-opus-4-5
- claude-sonnet-4-5
- claude-haiku-4-5

### Codex CLI（OpenAI chat，wire_api=chat）
- gpt-5.2
- gpt-5.2-codex
- gpt-4o
- claude-opus-4-5
- gemini-2.5-pro

## 映射配置模板
在“设置”页的 model_mapping_rules 填入 JSON。示例：

```json
{
  "anthropic": [
    {
      "source": "claude-opus-4-5",
      "target_provider": "Gemini-Antigravity",
      "target_model": "gemini-3-pro-high"
    },
    {
      "source": "claude-sonnet-4-5",
      "target_provider": "Gemini-Antigravity",
      "target_model": "gemini-2.5-pro"
    },
    {
      "source": "claude-haiku-4-5",
      "target_provider": "Gemini-Antigravity",
      "target_model": "gemini-2.5-flash"
    }
  ],
  "openai_chat": [
    {
      "source": "gpt-5.2",
      "target_provider": "Gemini-Antigravity",
      "target_model": "gemini-3-pro-high"
    },
    {
      "source": "gpt-5.2-codex",
      "target_provider": "Gemini-Antigravity",
      "target_model": "gemini-3-pro-high"
    },
    {
      "source": "gpt-4o",
      "target_provider": "Gemini-Antigravity",
      "target_model": "gemini-2.5-pro"
    }
  ]
}
```

## 配置与使用要点
- 映射是“严格等值匹配”，大小写不敏感。
- 映射缺失或 JSON 无效时，Anthropic 走内置硬编码映射；OpenAI chat 走原始模型名。
- 目标提供商仅支持 Gemini / Gemini-Antigravity。其他值会被忽略。

## 验证建议
- Claude Code：使用 model=claude-opus-4-5 调用 /v1/messages，观察日志与实际输出。
- Codex CLI：使用 wire_api=chat、model=gpt-5.2 调用 /v1/chat/completions，观察日志与实际输出。
- 如与预期不符，优先调整映射规则与模型目标。
