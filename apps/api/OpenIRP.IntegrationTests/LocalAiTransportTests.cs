using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OpenIRP.Application.Assistance;
using OpenIRP.Infrastructure.Assistance;
using Xunit;
namespace OpenIRP.IntegrationTests;

public sealed class LocalAiTransportTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handle):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Interlocked.Increment(ref Calls);return handle(request,ct);}
    }
    private static AssistanceInput Input()=>new(OllamaLocalAssistant.SchemaVersion,"PHISHING",null,null,null,null,null,null,null,null,"A","DETECTION","Revisar los datos sin ejecutar acciones","PENDING",true,true,true,[],[]);
    private static HttpResponseMessage JsonResponse(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
    private static HttpResponseMessage Tags()=>JsonResponse(new{models=new[]{new{name="qwen3:4b-instruct",size=2497293803L,digest=new string('a',64)}}});
    private static string Good()=>JsonSerializer.Serialize(new{stepCode="A",guidance="Explicacion sujeta a revision humana",cautions=new[]{"No omitir controles"},assumptions=Array.Empty<string>()});
    private static HttpResponseMessage Envelope(string content,string model="qwen3:4b-instruct",bool done=true,string doneReason="stop",object? tools=null,string? thinking=null,string? reasoning=null)=>JsonResponse(new{model,done,done_reason=doneReason,message=new{role="assistant",content,tool_calls=tools,thinking,reasoning_content=reasoning}});
    private static OllamaLocalAssistant Client(HttpClient http,Dictionary<string,string?>? changes=null,LocalAiConcurrency? lane=null)
    {
        var config=new Dictionary<string,string?>{["LocalAI:Enabled"]="true",["LocalAI:Model"]="qwen3:4b-instruct",["LocalAI:BaseUrl"]="http://127.0.0.1:11435",["LocalAI:TimeoutSeconds"]="1"};
        if(changes is not null)foreach(var pair in changes)config[pair.Key]=pair.Value;
        return new(http,new ConfigurationBuilder().AddInMemoryCollection(config).Build(),lane??new());
    }
    [Fact,Trait("Feature","LocalAiTransport")]
    public async Task StructuredRequestUsesFixedControlsAndNoToolsOrUserCredentials()
    {
        string? posted=null;var handler=new Handler(async(request,ct)=>{if(request.Method==HttpMethod.Get)return Tags();posted=await request.Content!.ReadAsStringAsync(ct);return Envelope(Good());});
        using var http=new HttpClient(handler);var result=await Client(http).ExplainAsync(Input());Assert.Equal("VALID",result.Outcome);Assert.Equal(new string('a',64),result.ModelDigest);
        using var request=JsonDocument.Parse(posted!);var root=request.RootElement;Assert.False(root.GetProperty("stream").GetBoolean());Assert.False(root.GetProperty("think").GetBoolean());Assert.EndsWith("/no_think",root.GetProperty("messages")[1].GetProperty("content").GetString());Assert.Equal(384,root.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.Equal(0,root.GetProperty("options").GetProperty("temperature").GetInt32());Assert.False(root.TryGetProperty("tools",out _));
        Assert.False(root.GetProperty("format").GetProperty("additionalProperties").GetBoolean());Assert.Equal(2,handler.Calls);
    }
    [Theory,Trait("Feature","LocalAiTransport")]
    [InlineData("json")] [InlineData("extra")] [InlineData("duplicate")] [InlineData("wrong-step")] [InlineData("empty")] [InlineData("html")] [InlineData("control")]
    [InlineData("model")] [InlineData("truncated")] [InlineData("unfinished")] [InlineData("tools")] [InlineData("oversize")] [InlineData("reasoning")] [InlineData("reasoning-content")] [InlineData("raw-think")]
    public async Task InvalidModelDataIsDiscardedWithoutReturningRawContent(string invalid)
    {
        var raw=invalid switch{
            "json"=>"PRIVATE_RAW_INVALID",
            "raw-think"=>"<think>PRIVATE_RAW_INVALID</think>"+Good(),
            "extra"=>"{\"stepCode\":\"A\",\"guidance\":\"PRIVATE_RAW_INVALID\",\"cautions\":[],\"assumptions\":[],\"requiresEvidence\":false}",
            "duplicate"=>"{\"stepCode\":\"A\",\"guidance\":\"one\",\"guidance\":\"PRIVATE_RAW_INVALID\",\"cautions\":[],\"assumptions\":[]}",
            _=>JsonSerializer.Serialize(new{stepCode=invalid=="wrong-step"?"B":"A",guidance=invalid switch{"empty"=>" ","html"=>"<script>PRIVATE_RAW_INVALID</script>","control"=>"Private\u001b",_=>"PRIVATE_RAW_INVALID"},cautions=Array.Empty<string>(),assumptions=Array.Empty<string>()})};
        var handler=new Handler((request,_)=>Task.FromResult(request.Method==HttpMethod.Get?Tags():invalid=="oversize"?new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',70000))}:Envelope(raw,invalid=="model"?"remote-model":"qwen3:4b-instruct",invalid!="unfinished",invalid=="truncated"?"length":"stop",invalid=="tools"?new[]{new{name="execute"}}:null,invalid=="reasoning"?"PRIVATE_RAW_INVALID":null,invalid=="reasoning-content"?"PRIVATE_RAW_INVALID":null)));
        using var http=new HttpClient(handler);var result=await Client(http).ExplainAsync(Input());Assert.Equal("INVALID",result.Outcome);Assert.Null(result.Recommendation);Assert.DoesNotContain("PRIVATE_RAW_INVALID",JsonSerializer.Serialize(result));
    }
    [Theory,Trait("Feature","LocalAiTransport")]
    [InlineData("http://example.test")] [InlineData("http://127.0.0.1:11435/secret")] [InlineData("http://user:password@127.0.0.1:11435")] [InlineData("http://localhost:11435")]
    public async Task RemoteOrAmbiguousEndpointsNeverReceiveRequests(string endpoint)
    {
        var handler=new Handler((_,_)=>throw new InvalidOperationException("Network must not be used"));using var http=new HttpClient(handler);
        var reply=await Client(http,new(){["LocalAI:BaseUrl"]=endpoint}).ExplainAsync(Input());Assert.Equal("ai_invalid_configuration",reply.ErrorCode);Assert.Equal(0,handler.Calls);
    }
    [Theory,Trait("Feature","LocalAiTransport")]
    [InlineData("disabled")] [InlineData("cloud")] [InlineData("missing")] [InlineData("redirect")] [InlineData("connection")] [InlineData("timeout")] [InlineData("base-model")]
    public async Task UnavailableInferenceHasBoundedSafeFailure(string failure)
    {
        var handler=new Handler(async(request,ct)=>{
            if(failure=="timeout"){await Task.Delay(Timeout.InfiniteTimeSpan,ct);throw new InvalidOperationException();}
            if(failure=="connection")throw new HttpRequestException("PRIVATE_PROVIDER_DETAIL");
            if(failure=="redirect")return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect){Headers={Location=new Uri("https://example.test")}};
            if(failure=="missing")return JsonResponse(new{models=Array.Empty<object>()});
            return request.Method==HttpMethod.Get?Tags():Envelope(Good());});
        using var http=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};var changes=new Dictionary<string,string?>();
        if(failure=="disabled")changes["LocalAI:Enabled"]="false";if(failure=="cloud")changes["LocalAI:Model"]="some-model:cloud";if(failure=="base-model")changes["LocalAI:Model"]="qwen3:4b";
        var result=await Client(http,changes).ExplainAsync(Input());Assert.Equal("UNAVAILABLE",result.Outcome);Assert.Null(result.Recommendation);Assert.DoesNotContain("PRIVATE_PROVIDER_DETAIL",JsonSerializer.Serialize(result));
        if(failure is "cloud" or "disabled" or "base-model")Assert.Equal(0,handler.Calls);
    }
    [Fact,Trait("Feature","LocalAiTransport")]
    public async Task LocalInferenceHasNoQueueAndCallerCancellationReleasesItsSlot()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new Handler(async(_,ct)=>{entered.TrySetResult();await Task.Delay(Timeout.InfiniteTimeSpan,ct);throw new InvalidOperationException();});
        using var http=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};var lane=new LocalAiConcurrency();var client=Client(http,lane:lane);
        using var cancel=new CancellationTokenSource();var first=client.ExplainAsync(Input(),cancel.Token);await entered.Task;
        Assert.Equal("ai_busy",(await client.ExplainAsync(Input())).ErrorCode);cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
        var healthy=new Handler((request,_)=>Task.FromResult(request.Method==HttpMethod.Get?Tags():Envelope(Good())));using var other=new HttpClient(healthy);
        Assert.Equal("VALID",(await Client(other,lane:lane).ExplainAsync(Input())).Outcome);
    }
    [Fact,Trait("Feature","LocalAiTransport")]
    public async Task OversizedInputIsRejectedBeforeAnyNetworkRequest()
    {
        var handler=new Handler((_,_)=>throw new InvalidOperationException("Unexpected network"));using var http=new HttpClient(handler);
        var result=await Client(http).ExplainAsync(Input() with{Instructions=new string('a',3500)});
        Assert.Equal("ai_context_too_large",result.ErrorCode);Assert.Equal("INVALID",result.Outcome);Assert.Equal(0,handler.Calls);
    }

}
