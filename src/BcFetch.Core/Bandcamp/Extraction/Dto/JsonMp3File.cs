using System.Text.Json.Serialization;

namespace BcFetch.Bandcamp.Extraction.Dto;

internal sealed class JsonMp3File
{
    [JsonPropertyName("mp3-128")]
    public string Url { get; set; }
}
