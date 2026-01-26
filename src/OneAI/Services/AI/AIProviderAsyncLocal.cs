namespace OneAI.Services.AI;


public class AIProviderAsyncLocal
{
    private static readonly AsyncLocal<AIProviderHolder> _AIProviderHolder = new();

    public static List<int> AIProviderIds
    {
        get
        {
            // 确保 Holder 存在，避免返回临时列表导致 Add 操作丢失
            _AIProviderHolder.Value ??= new AIProviderHolder();
            return _AIProviderHolder.Value.AIProviderIds;
        }
        set
        {
            _AIProviderHolder.Value ??= new AIProviderHolder();
            _AIProviderHolder.Value.AIProviderIds = value;
        }
    }

    private sealed class AIProviderHolder
    {
        /// <summary>
        /// 已经试用的渠道ID
        /// </summary>
        public List<int> AIProviderIds { get; set; } = new(5);
    }
}