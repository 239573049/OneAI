namespace OneAI.Models;

/// <summary>
/// 统一 API 响应格式
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// 状态码（0 表示成功）
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// 数据
    /// </summary>
    public T? Data { get; set; }

    public ApiResponse(int code, string message, T? data = default)
    {
        Code = code;
        Message = message;
        Data = data;
    }

    /// <summary>
    /// 成功响应
    /// </summary>
    public static ApiResponse<T> Success(T data, string message = "操作成功")
    {
        return new ApiResponse<T>(0, message, data);
    }

    /// <summary>
    /// 失败响应
    /// </summary>
    public static ApiResponse<T> Fail(string message, int code = 1)
    {
        return new ApiResponse<T>(code, message);
    }
}

/// <summary>
/// 无数据的 API 响应
/// </summary>
public class ApiResponse : ApiResponse<object>
{
    public ApiResponse(int code, string message) : base(code, message, null)
    {
    }

    /// <summary>
    /// 成功响应
    /// </summary>
    public static new ApiResponse Success(string message = "操作成功")
    {
        return new ApiResponse(0, message);
    }

    /// <summary>
    /// 失败响应
    /// </summary>
    public static new ApiResponse Fail(string message, int code = 1)
    {
        return new ApiResponse(code, message);
    }
}
