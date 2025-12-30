import { useEffect, useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { AlertCircle, Check, Clipboard, Save, Trash2, Plus } from 'lucide-react'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/animate-ui/components/card'
import { Button } from '@/components/animate-ui/components/buttons/button'
import { Label } from '@/components/animate-ui/components/label'
import { Input } from '@/components/animate-ui/components/input'
import { settingsService } from '@/services/settings'
import type { SystemSettingsDto } from '@/types/settings'

const mappingKey = 'model_mapping_rules'

type MappingMode = 'table' | 'json'

type MappingRule = {
  source: string
  target_provider: string
  target_model: string
}

type MappingConfig = {
  anthropic: MappingRule[]
  openai_chat: MappingRule[]
}

const providerOptions = [
  { label: '不指定（默认）', value: '' },
  { label: 'Gemini', value: 'Gemini' },
  { label: 'Gemini-Antigravity', value: 'Gemini-Antigravity' },
]

const templateConfig: MappingConfig = {
  anthropic: [
    {
      source: 'claude-opus-4-5',
      target_provider: 'Gemini-Antigravity',
      target_model: 'gemini-3-pro-high',
    },
  ],
  openai_chat: [
    {
      source: 'gpt-5.2',
      target_provider: 'Gemini-Antigravity',
      target_model: 'gemini-3-pro-high',
    },
  ],
}

const templateJson = JSON.stringify(templateConfig, null, 2)

const normalizeRule = (rule: Partial<MappingRule> | null | undefined): MappingRule => {
  return {
    source: rule?.source?.toString().trim() ?? '',
    target_provider: rule?.target_provider?.toString().trim() ?? '',
    target_model: rule?.target_model?.toString().trim() ?? '',
  }
}

const normalizeConfig = (value: unknown): MappingConfig => {
  if (!value || typeof value !== 'object') {
    return { anthropic: [], openai_chat: [] }
  }

  const record = value as Record<string, unknown>
  const anthropic = Array.isArray(record.anthropic)
    ? record.anthropic.map((rule) => normalizeRule(rule as MappingRule))
    : []
  const openai_chat = Array.isArray(record.openai_chat)
    ? record.openai_chat.map((rule) => normalizeRule(rule as MappingRule))
    : []

  return { anthropic, openai_chat }
}

const validateConfig = (config: MappingConfig) => {
  const errors: string[] = []
  const allowedProviders = providerOptions.map((item) => item.value).filter(Boolean)

  const checkRules = (rules: MappingRule[], label: string) => {
    rules.forEach((rule, index) => {
      if (!rule.source.trim()) {
        errors.push(`${label} 第 ${index + 1} 行缺少 source`) 
      }
      if (!rule.target_model.trim()) {
        errors.push(`${label} 第 ${index + 1} 行缺少 target_model`)
      }
      if (rule.target_provider && !allowedProviders.includes(rule.target_provider)) {
        errors.push(`${label} 第 ${index + 1} 行 target_provider 无效`)
      }
    })
  }

  checkRules(config.anthropic, 'Anthropic')
  checkRules(config.openai_chat, 'OpenAI Chat')

  return errors
}

export default function ModelMappingPage() {
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [mode, setMode] = useState<MappingMode>('table')
  const [rawValue, setRawValue] = useState('')
  const [config, setConfig] = useState<MappingConfig>(templateConfig)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<SystemSettingsDto | null>(null)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    loadMapping()
  }, [])

  const loadMapping = async () => {
    try {
      setLoading(true)
      setError(null)
      const setting = await settingsService.getSetting(mappingKey)
      setInfo(setting)
      const value = setting?.value || templateJson
      setRawValue(value)
      try {
        const parsed = JSON.parse(value)
        setConfig(normalizeConfig(parsed))
      } catch {
        setConfig(templateConfig)
        setError('当前配置不是有效 JSON，已加载模板')
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '加载映射配置失败'
      setError(message)
    } finally {
      setLoading(false)
    }
  }

  const parsedError = useMemo(() => {
    if (!rawValue.trim()) {
      return '配置内容不能为空'
    }
    try {
      JSON.parse(rawValue)
      return null
    } catch (err) {
      if (err instanceof Error) {
        return err.message
      }
      return 'JSON 格式无效'
    }
  }, [rawValue])

  const handleModeChange = (nextMode: MappingMode) => {
    if (nextMode === mode) {
      return
    }

    if (nextMode === 'json') {
      setRawValue(JSON.stringify(config, null, 2))
      setMode('json')
      return
    }

    if (parsedError) {
      setError(parsedError)
      return
    }

    try {
      const parsed = JSON.parse(rawValue)
      setConfig(normalizeConfig(parsed))
      setMode('table')
    } catch (err) {
      const message = err instanceof Error ? err.message : 'JSON 解析失败'
      setError(message)
    }
  }

  const updateRule = (
    group: keyof MappingConfig,
    index: number,
    field: keyof MappingRule,
    value: string
  ) => {
    setConfig((prev) => {
      const nextRules = [...prev[group]]
      const target = { ...nextRules[index], [field]: value }
      nextRules[index] = normalizeRule(target)
      return { ...prev, [group]: nextRules }
    })
  }

  const addRule = (group: keyof MappingConfig) => {
    setConfig((prev) => ({
      ...prev,
      [group]: [
        ...prev[group],
        { source: '', target_provider: '', target_model: '' },
      ],
    }))
  }

  const removeRule = (group: keyof MappingConfig, index: number) => {
    setConfig((prev) => ({
      ...prev,
      [group]: prev[group].filter((_, idx) => idx !== index),
    }))
  }

  const handleSave = async () => {
    const valueToSave = mode === 'json' ? rawValue : JSON.stringify(config, null, 2)

    if (mode === 'json' && parsedError) {
      setError(parsedError)
      return
    }

    if (mode === 'table') {
      const errors = validateConfig(config)
      if (errors.length > 0) {
        setError(errors[0])
        return
      }
    }

    try {
      setSaving(true)
      setError(null)
      await settingsService.updateSetting(mappingKey, {
        value: valueToSave,
        description: info?.description ?? '模型映射规则（JSON），用于 Anthropic 与 OpenAI Chat 的模型别名映射',
      })
      setRawValue(valueToSave)
      await loadMapping()
    } catch (err) {
      const message = err instanceof Error ? err.message : '保存失败'
      setError(message)
    } finally {
      setSaving(false)
    }
  }

  const handleFormat = () => {
    if (parsedError) {
      setError(parsedError)
      return
    }

    try {
      const obj = JSON.parse(rawValue)
      setRawValue(JSON.stringify(obj, null, 2))
    } catch (err) {
      const message = err instanceof Error ? err.message : '格式化失败'
      setError(message)
    }
  }

  const handleCopyTemplate = async () => {
    try {
      await navigator.clipboard.writeText(templateJson)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch (err) {
      setError(err instanceof Error ? err.message : '复制失败')
    }
  }

  const renderRules = (group: keyof MappingConfig, title: string, description: string) => {
    return (
      <Card variant="elevated">
        <CardHeader>
          <CardTitle className="text-base">{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          {config[group].length === 0 ? (
            <p className="text-sm text-muted-foreground">暂无规则</p>
          ) : (
            <div className="space-y-2">
              {config[group].map((rule, index) => (
                <div
                  key={`${group}-${index}`}
                  className="grid grid-cols-1 gap-2 md:grid-cols-12 md:items-end"
                >
                  <div className="md:col-span-4">
                    <Label className="text-xs text-muted-foreground">source</Label>
                    <Input
                      value={rule.source}
                      onChange={(event) => updateRule(group, index, 'source', event.target.value)}
                      placeholder="例如：claude-opus-4-5"
                    />
                  </div>
                  <div className="md:col-span-4">
                    <Label className="text-xs text-muted-foreground">target_provider</Label>
                    <select
                      value={rule.target_provider}
                      onChange={(event) => updateRule(group, index, 'target_provider', event.target.value)}
                      className="mt-2 w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
                    >
                      {providerOptions.map((item) => (
                        <option key={item.value || 'empty'} value={item.value}>
                          {item.label}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="md:col-span-3">
                    <Label className="text-xs text-muted-foreground">target_model</Label>
                    <Input
                      value={rule.target_model}
                      onChange={(event) => updateRule(group, index, 'target_model', event.target.value)}
                      placeholder="例如：gemini-3-pro-high"
                    />
                  </div>
                  <div className="md:col-span-1">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => removeRule(group, index)}
                      className="w-full"
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
          <Button variant="outline" onClick={() => addRule(group)} className="gap-2">
            <Plus className="h-4 w-4" />
            新增规则
          </Button>
        </CardContent>
      </Card>
    )
  }

  return (
    <div className="p-6 space-y-6">
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
        className="space-y-2"
      >
        <h2 className="text-3xl font-bold">模型映射配置</h2>
        <p className="text-muted-foreground">
          提供表格模式与 JSON 模式，保存后立即生效。
        </p>
      </motion.div>

      {error && (
        <motion.div
          initial={{ opacity: 0, y: -10 }}
          animate={{ opacity: 1, y: 0 }}
          className="flex gap-3 rounded-lg border border-red-200 bg-red-50 p-4 text-red-800"
        >
          <AlertCircle className="h-5 w-5 mt-0.5 flex-shrink-0" />
          <div>
            <p className="font-medium">配置错误</p>
            <p className="text-sm">{error}</p>
          </div>
        </motion.div>
      )}

      <div className="flex flex-wrap gap-2">
        <Button
          variant={mode === 'table' ? 'default' : 'outline'}
          onClick={() => handleModeChange('table')}
        >
          表格模式
        </Button>
        <Button
          variant={mode === 'json' ? 'default' : 'outline'}
          onClick={() => handleModeChange('json')}
        >
          JSON 模式
        </Button>
        <div className="flex-1" />
        <Button variant="outline" onClick={handleCopyTemplate} className="gap-2">
          {copied ? <Check className="h-4 w-4" /> : <Clipboard className="h-4 w-4" />}
          复制模板
        </Button>
        <Button onClick={handleSave} disabled={saving} className="gap-2">
          <Save className="h-4 w-4" />
          {saving ? '保存中...' : '保存'}
        </Button>
      </div>

      {loading ? (
        <p className="text-muted-foreground text-sm">加载中...</p>
      ) : mode === 'table' ? (
        <div className="space-y-4">
          {renderRules('anthropic', 'Anthropic 映射', '用于 Claude Code（/v1/messages）。')}
          {renderRules('openai_chat', 'OpenAI Chat 映射', '用于 Codex CLI（wire_api=chat）。')}
        </div>
      ) : (
        <Card variant="elevated">
          <CardHeader>
            <CardTitle className="text-base">映射 JSON</CardTitle>
            <CardDescription>
              键名包含 anthropic 与 openai_chat。可参考文档：docs/model-catalog.md。
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <Label className="text-xs text-muted-foreground">配置内容</Label>
              <textarea
                value={rawValue}
                onChange={(event) => setRawValue(event.target.value)}
                className="mt-2 w-full min-h-[320px] rounded-md border border-input bg-background px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-primary"
                placeholder="请输入 JSON"
              />
              {parsedError ? (
                <p className="text-xs text-red-600 mt-1">JSON 无效：{parsedError}</p>
              ) : (
                <p className="text-xs text-green-600 mt-1">JSON 格式有效</p>
              )}
            </div>
            <Button variant="outline" onClick={handleFormat}>格式化</Button>
          </CardContent>
        </Card>
      )}

      <Card variant="elevated">
        <CardHeader>
          <CardTitle className="text-base">配置说明</CardTitle>
          <CardDescription>保存后服务会从数据库读取并缓存。</CardDescription>
        </CardHeader>
        <CardContent className="space-y-2 text-sm">
          <p>1. source 为客户端填写的模型名（大小写不敏感）。</p>
          <p>2. target_provider 仅支持 Gemini 或 Gemini-Antigravity。</p>
          <p>3. target_model 必须来自 Antigravity 可用模型列表。</p>
        </CardContent>
      </Card>
    </div>
  )
}
