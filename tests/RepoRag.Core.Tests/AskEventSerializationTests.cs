using System.Text.Json;
using RepoRag.Core.Asking;

namespace RepoRag.Core.Tests;

// ADR 0007: the page switches on "type". SignalR serialises each stream item as its runtime type
// (as object), so the field must be there even then.
public class AskEventSerializationTests
{
    private static readonly JsonSerializerOptions SignalRLike = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("text")]
    [InlineData("searching")]
    [InlineData("sources")]
    [InlineData("done")]
    [InlineData("error")]
    public void Every_event_carries_its_type_when_serialised_as_object(string type)
    {
        AskEvent e = type switch
        {
            "text" => new TextEvent("Hi"),
            "searching" => new SearchingEvent("q"),
            "sources" => new SourcesEvent([], []),
            "done" => new DoneEvent("id", 0),
            _ => new ErrorEvent("oops"),
        };

        var json = JsonSerializer.Serialize((object)e, SignalRLike);

        Assert.Contains($"\"type\":\"{type}\"", json);
    }
}
