using Microsoft.EntityFrameworkCore;
using OneAI.Entities;

namespace OneAI.Data;

/// <summary>
/// 日志专用数据库上下文 - 独立连接，避免影响主业务
/// </summary>
public class LogDbContext : DbContext
{
    public LogDbContext(DbContextOptions<LogDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// AI请求日志表
    /// </summary>
    public DbSet<AIRequestLog> AIRequestLogs { get; set; }

    /// <summary>
    /// AI请求每小时汇总统计 - 总体维度
    /// </summary>
    public DbSet<AIRequestHourlySummary> HourlySummaries { get; set; }

    /// <summary>
    /// AI请求每小时汇总统计 - 按模型维度
    /// </summary>
    public DbSet<AIRequestHourlyByModel> HourlyByModels { get; set; }

    /// <summary>
    /// AI请求每小时汇总统计 - 按账户维度
    /// </summary>
    public DbSet<AIRequestHourlyByAccount> HourlyByAccounts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置 AIRequestLog 实体（与 AppDbContext 保持一致）
        modelBuilder.Entity<AIRequestLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            // 字符串字段长度限制
            entity.Property(e => e.RequestId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ConversationId).HasMaxLength(100);
            entity.Property(e => e.SessionId).HasMaxLength(100);
            entity.Property(e => e.Provider).HasMaxLength(50);
            entity.Property(e => e.Model).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(5000);
            entity.Property(e => e.QuotaInfo).HasMaxLength(2000);
            entity.Property(e => e.ClientIp).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.Originator).HasMaxLength(100);
            entity.Property(e => e.ExtensionData); // 无长度限制（TEXT）

            // 注意：不配置外键关系，因为 AIAccount 在主数据库中
            // 只保留 AccountId 作为普通字段
            // 显式忽略导航属性，避免 EF Core 自动创建外键
            entity.Ignore(e => e.Account);

            // 创建索引以提高查询性能
            entity.HasIndex(e => e.RequestId).IsUnique(); // 请求ID唯一
            entity.HasIndex(e => e.ConversationId); // 按会话查询
            entity.HasIndex(e => e.AccountId); // 按账户查询
            entity.HasIndex(e => e.Model); // 按模型查询
            entity.HasIndex(e => e.IsSuccess); // 按成功状态查询
            entity.HasIndex(e => e.RequestStartTime); // 按时间范围查询
            entity.HasIndex(e => new { e.AccountId, e.RequestStartTime }); // 组合索引：账户+时间
            entity.HasIndex(e => new { e.Model, e.RequestStartTime }); // 组合索引：模型+时间
        });

        // 配置 AIRequestHourlySummary 实体
        modelBuilder.Entity<AIRequestHourlySummary>(entity =>
        {
            entity.HasKey(e => e.Id);

            // 唯一索引：防止同一小时重复聚合
            entity.HasIndex(e => e.HourStartTime).IsUnique();

            // 时间范围查询索引
            entity.HasIndex(e => new { e.HourStartTime, e.TotalRequests });
        });

        // 配置 AIRequestHourlyByModel 实体
        modelBuilder.Entity<AIRequestHourlyByModel>(entity =>
        {
            entity.HasKey(e => e.Id);

            // 唯一复合索引：同一小时+同一模型只有一条记录
            entity.HasIndex(e => new { e.HourStartTime, e.Model }).IsUnique();

            // 查询索引
            entity.HasIndex(e => e.Model);
            entity.HasIndex(e => e.Provider);
            entity.HasIndex(e => new { e.HourStartTime, e.Provider });

            // 字符串长度限制
            entity.Property(e => e.Model).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(50);
        });

        // 配置 AIRequestHourlyByAccount 实体
        modelBuilder.Entity<AIRequestHourlyByAccount>(entity =>
        {
            entity.HasKey(e => e.Id);

            // 唯一复合索引：同一小时+同一账户只有一条记录
            entity.HasIndex(e => new { e.HourStartTime, e.AccountId }).IsUnique();

            // 查询索引
            entity.HasIndex(e => e.AccountId);
            entity.HasIndex(e => e.Provider);
            entity.HasIndex(e => new { e.Provider, e.HourStartTime });

            // 字符串长度限制
            entity.Property(e => e.AccountName).HasMaxLength(100);
            entity.Property(e => e.Provider).HasMaxLength(50);
        });
    }
}
