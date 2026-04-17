using FeishuNetSdk;

namespace FeishuAdaptor.Helper;

public static class FeishuHelper
{
    public static FeishuResponse<T> ThrowIfFeishuResponseNotSuccess<T>(this FeishuResponse<T> response)
    {
        if (response.IsSuccess)
            return response;

        throw new FeishuRequestException()
        {
            Code = response.Code!.Value,
            Msg = response.Msg,
            ErrorMessage = response.Error?.Message ?? "unknown error",
            LogId = response.Error?.LogId ?? "unkonwn log id",
        };
    }
}

public class FeishuRequestException : Exception
{
    public int Code { get; set; }

    public required string Msg { get; set; }

    public required string ErrorMessage { get; set; }

    public required string LogId { get; set; }

    public override string Message =>
        $"FeishuRequestException: Code={Code}, Msg={Msg}, ErrorMessage={ErrorMessage}, LogId={LogId}";
}
