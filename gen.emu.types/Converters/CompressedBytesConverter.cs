using common.utils;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Converters;

public class CompressedBytesConverterFactory : JsonConverterFactory
{
  public override bool CanConvert(Type typeToConvert) =>
      typeof(IEnumerable<byte>).IsAssignableFrom(typeToConvert);

  public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
  {
    return new CompressedBytesConverter();
  }

}

public class CompressedBytesConverter : JsonConverter<IEnumerable<byte>>
{
  public override void Write(Utf8JsonWriter writer, IEnumerable<byte> value, JsonSerializerOptions options)
  {
    var compressed = Utils.CompressData(value);
    string base64str = Convert.ToBase64String(compressed);
    writer.WriteStringValue(base64str);
  }

  public override IEnumerable<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    var base64str = reader.GetString();
    if (string.IsNullOrEmpty(base64str))
    {
      return new List<byte>();
    }

    var compressed = Convert.FromBase64String(base64str);
    var decompressed = Utils.DecompressData(compressed);
    return decompressed.ToList();
  }

}
