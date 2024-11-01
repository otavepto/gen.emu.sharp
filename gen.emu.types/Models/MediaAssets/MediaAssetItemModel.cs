using gen.emu.types.Converters;
using System.Text.Json.Serialization;

namespace gen.emu.types.Models.MediaAssets;

public class MediaAssetItemModel
{
    public string Name { get; set; } = string.Empty;

    public string NameOnDisk { get; set; } = string.Empty;

    //[JsonInclude]
    [JsonIgnore]
    [JsonConverter(typeof(CompressedBytesConverterFactory))]
    public IReadOnlyList<byte> Data { get; private set; } = new List<byte>();

}
