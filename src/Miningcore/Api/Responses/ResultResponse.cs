namespace Miningcore.Api.Responses;

public class ResultResponse<T>
{
    public ResultResponse(T result)
    {
        Result = result;
        Success = result != null;
    }

    public ResultResponse()
    {
        Success = true;
    }

    public T Result { get; set; }
    public bool Success { get; set; }
}
