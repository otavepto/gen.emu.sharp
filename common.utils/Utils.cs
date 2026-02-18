using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading.Tasks;
using System.Globalization;
using System.Web;
using System.Reflection;
using System.IO.Compression;
using System.Text.Encodings.Web;


namespace common.utils;

public static class Utils
{

  public static Encoding Utf8EncodingNoBom { get; } = new UTF8Encoding(false);

  public static string GetExeDir(bool relative = false)
  {
    if (relative)
    {
      return Directory.GetCurrentDirectory();
    }
    return AppContext.BaseDirectory;
  }

  public static bool TryClear<Tval>(this IEnumerable<Tval> dest)
  {
    if (dest is null)
    {
      return false;
    }

    if (dest is ICollection<Tval> icdest && !icdest.IsReadOnly)
    {
      icdest.Clear();
      return true;
    }

    return false;
  }

  public static void CopyFrom<Tkey, Tval>(this IEnumerable<KeyValuePair<Tkey, Tval>> dest, IEnumerable<KeyValuePair<Tkey, Tval>> src)
  {
    if (dest is null ||  src is null)
    {
      return;
    }

    if (dest is IDictionary<Tkey, Tval> iddest && !iddest.IsReadOnly)
    {
      foreach (var item in src)
      {
        iddest[item.Key] = item.Value;
      }
    }
    else
    {
      throw new ArgumentException("Destination is not copyable");
    }
  }

  public static void CopyFrom<Tval>(this IEnumerable<Tval> dest, IEnumerable<Tval> src)
  {
    if (dest is null || src is null)
    {
      return;
    }

    if (dest is List<Tval> ldest)
    {
      ldest.AddRange(src);
    }
    else if (dest is ICollection<Tval> icdest && !icdest.IsReadOnly)
    {
      foreach (var item in src)
      {
        icdest.Add(item);
      }
    }
    else
    {
      throw new ArgumentException("Destination is not copyable");
    }
  }

  public static JsonNode? GetKeyIgnoreCase(this JsonNode? obj, params string[] keys)
  {
    if (keys is null || keys.Length == 0)
    {
      return null;
    }

    int idx = 0;
    while (idx < keys.Length)
    {
      if (obj is null || obj.GetValueKind() != JsonValueKind.Object)
      {
        return null;
      }

      var currentObj = obj.AsObject();
      var objDict = currentObj
        .GroupBy(kv => kv.Key.ToUpperInvariant(), kv => (ActualKey: kv.Key, ActualObj: kv.Value)) // upper key <> [list of actual values]
        .ToDictionary(g => g.Key, g => g.ToList());
      var currentKey = keys[idx];
      if (objDict.Count == 0 || !objDict.TryGetValue(currentKey.ToUpperInvariant(), out var objList) || objList.Count == 0)
      {
        return null;
      }

      obj = null;
      foreach (var (actualKey, actualObj) in objList)
      {
        if (string.Equals(currentKey, actualKey, StringComparison.Ordinal))
        {
          obj = actualObj;
          break;
        }
      }

      idx++;
    }

    return obj;
  }

  public enum WebMethod
  {
    Get,
    Post,
  }

  public static async Task<byte[]> WebRequestAsync(string url, WebMethod method, JsonObject? urlParams = null, JsonObject? postData = null, CancellationToken cancelToken = default)
  {
    if (string.IsNullOrEmpty(url))
    {
      throw new ArgumentNullException(nameof(url));
    }

    using var clientConfig = new HttpClientHandler
    {
      AllowAutoRedirect = true,
      CheckCertificateRevocationList = false,
      MaxAutomaticRedirections = 100,
      ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true
    };

    if (urlParams is not null && urlParams.Count > 0)
    {
      var uriBuilder = new UriBuilder(url);
      var queryParams = HttpUtility.ParseQueryString(string.Empty);
      foreach (var kv in urlParams)
      {
        if (string.IsNullOrEmpty(kv.Key))
        {
          continue;
        }
        var val = kv.Value?.ToString();
        if (string.IsNullOrEmpty(val))
        {
          continue;
        }
        queryParams[kv.Key] = val;
      }

      uriBuilder.Query = queryParams.ToString();
      url = uriBuilder.ToString();
    }

    using var client = new HttpClient(clientConfig);
    switch (method)
    {
      case WebMethod.Get:
        {
          using var response = await client.GetAsync(url, cancelToken).ConfigureAwait(false);
          response.EnsureSuccessStatusCode();
          return await response.Content.ReadAsByteArrayAsync(cancelToken).ConfigureAwait(false);
        }
      case WebMethod.Post:
        {
          string serializedData = postData is null ? string.Empty : postData.ToJsonString();
          var content = new StringContent(serializedData, Utf8EncodingNoBom, "application/json");
          using var response = await client.PostAsync(url, content, cancelToken).ConfigureAwait(false);
          response.EnsureSuccessStatusCode();
          return await response.Content.ReadAsByteArrayAsync(cancelToken).ConfigureAwait(false);
        }
      default:
        throw new NotImplementedException($"Unknown method {method}");
    }

  }

  public static async Task<Tout[]> ParallelJobsAsync<Tin, Tout>(IEnumerable<Tin> inputs, Func<Tin, int, uint, CancellationToken, Task<Tout>> job, int maxParallelJobs = int.MaxValue, uint jobTrialsOnFailure = 0, CancellationToken cancelToken = default)
  {
    if (inputs is null || !inputs.Any() || job is null)
    {
      return [];
    }

    var options = new ParallelOptions
    {
      CancellationToken = cancelToken,
      MaxDegreeOfParallelism = maxParallelJobs,
    };

    var inputsList = inputs.ToList();
    var res = new Tout[inputsList.Count];
    var tasks = Parallel.ForAsync(0, inputsList.Count, options, async (jobIdx, ct) =>
    {
      uint attempts = jobTrialsOnFailure + 1; // +1 for the first normal run
      for (uint attemptIdx = 0; attemptIdx < attempts && !cancelToken.IsCancellationRequested; attemptIdx++)
      {
        try
        {
          var itemRes = await job(inputsList[jobIdx], jobIdx, attemptIdx, cancelToken).ConfigureAwait(false);
          res[jobIdx] = itemRes;
          break; // exit on success
        }
        catch
        {

        }
      }
    });

    await tasks.ConfigureAwait(false);
    return res;
  }

  public static Task ParallelJobsAsync<Tin>(IEnumerable<Tin> inputs, Func<Tin, int, uint, CancellationToken, Task> job, int maxParallelJobs = int.MaxValue, uint jobTrialsOnFailure = 0, CancellationToken cancelToken = default)
  {
    return ParallelJobsAsync<Tin, object?>(inputs, async (item, jobIdx, trialIdx, cancelToken) =>
    {
      await job(item, jobIdx, trialIdx, cancelToken).ConfigureAwait(false);
      return null;
    }, maxParallelJobs, jobTrialsOnFailure, cancelToken);
  }

  public static string GetLastUrlComponent(string url)
  {
    // "qwe/asd/my_pic.jpg/?t=1719426374"
    var allComponents = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    var nameComponent = allComponents.LastOrDefault(s => !s.Trim().StartsWith('?'));
    if (nameComponent is null)
    {
      if (allComponents.Length > 0)
      {
        return allComponents.Last();
      }
      return string.Empty;
    }

    // "qwe/asd/my_pic.jpg?t=1719426374"
    int queryIndex = nameComponent.IndexOf('?');
    return queryIndex != -1 ? nameComponent.Substring(0, queryIndex) : nameComponent;
  }

  public static bool ToBoolSafe(this JsonNode? obj)
  {
    if (obj is null)
    {
      return false;
    }

    switch (obj.GetValueKind())
    {
      case JsonValueKind.String:
        {
          var str = obj.ToString();
          if (string.IsNullOrEmpty(str))
          {
            return false;
          }
          return str.Equals("true", StringComparison.OrdinalIgnoreCase)
            || str.Equals("1", StringComparison.Ordinal);
        }
      case JsonValueKind.Number:
        {
          if (double.TryParse(obj.ToString() ?? string.Empty, CultureInfo.InvariantCulture, out var num) && !double.IsNaN(num))
          {
            const double ZERO_THRESHOLD = 1e-10;
            return Math.Abs(num) >= ZERO_THRESHOLD;
          }
        }
        break;
      case JsonValueKind.True: return true;
    }

    return false;
  }

  public static JsonArray ToArraySafe(this JsonNode? obj)
  {
    if (obj is null)
    {
      return [];
    }

    switch (obj.GetValueKind())
    {
      case JsonValueKind.Array: return obj.AsArray();
    }

    return [];
  }

  public static JsonObject ToObjSafe(this JsonNode? obj)
  {
    if (obj is null)
    {
      return new();
    }

    switch (obj.GetValueKind())
    {
      case JsonValueKind.Object: return obj.AsObject();
    }

    return new();
  }

  public static string ToStringSafe(this JsonNode? obj)
  {
    if (obj is null)
    {
      return string.Empty;
    }

    switch (obj.GetValueKind())
    {
      case JsonValueKind.String: return obj.ToString() ?? string.Empty;
    }

    return string.Empty;
  }

  public static double ToNumSafe(this JsonNode? obj)
  {
    if (TryConvertToNum(obj, out var num))
    {
      return num;
    }
    return 0;
  }

  public static bool TryConvertToNum(this JsonNode? obj, out double num)
  {
    if (obj is null)
    {
      num = double.NaN;
      return false;
    }

    switch (obj.GetValueKind())
    {
      case JsonValueKind.String:
      case JsonValueKind.Number:
        {
          if (
            double.TryParse(obj.ToString() ?? string.Empty, CultureInfo.InvariantCulture, out num)
            && !double.IsNaN(num)
          )
          {
            return true;
          }
        }
        break;
      case JsonValueKind.True:
        {
          num = 1;
          return true;
        }
      case JsonValueKind.False:
        {
          num = 0;
          return true;
        }
    }

    num = double.NaN;
    return false;
  }

  public static string SanitizeFilename(string filename)
  {
    if (string.IsNullOrEmpty(filename))
    {
      return string.Empty;
    }

    // Windows: https://github.com/dotnet/runtime/blob/50be35a211536209b3e1d68619f7d2f2bf3caa0a/src/libraries/System.Private.CoreLib/src/System/IO/Path.Windows.cs#L15
    // Linux: https://github.com/dotnet/runtime/blob/50be35a211536209b3e1d68619f7d2f2bf3caa0a/src/libraries/System.Private.CoreLib/src/System/IO/Path.Unix.cs#L12
    // Windows has more invalid chars, we want to use that in case we're on NTFS partition mounted in Linux
    static char[] WinInvalidFileNameChars() =>
    [
        '\"', '<', '>', '|', '\0',
        (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
        (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
        (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
        (char)31, ':', '*', '?', '\\', '/'
    ];

    return new string(filename.Where(c => !WinInvalidFileNameChars().Contains(c)).ToArray());
  }

  public static Tatt? GetEnumAttribute<Tatt, Tenum>(this Tenum val)
    where Tatt: Attribute
    where Tenum : Enum
  {
    var field = typeof(Tenum).GetField(val.ToString());
    if (field is null)
    {
      return null;
    }

    var att = field.GetCustomAttribute<Tatt>();
    return att;
  }

  public static byte[] CompressData(IEnumerable<byte> data)
  {
    if (data is null || !data.Any())
    {
      return [];
    }

    using var memoryStream = new MemoryStream();
    using (var compressStream = new GZipStream(memoryStream, CompressionLevel.SmallestSize))
    {
      compressStream.Write(data.ToArray(), 0, data.Count());
    }

    return memoryStream.ToArray();
  }

  public static byte[] DecompressData(IEnumerable<byte> compressedData)
  {
    if (compressedData is null || !compressedData.Any())
    {
      return [];
    }

    using var decompressedStream = new MemoryStream();
    using (var compressedStream = new MemoryStream(compressedData.ToArray()))
    {
      using var compressStream = new GZipStream(compressedStream, CompressionMode.Decompress);
      compressStream.CopyTo(decompressedStream);
    }
    return decompressedStream.ToArray();
  }

  public static void WriteJson<T>(T? obj, string filepath, bool asciiEscaping = false)
  {
    if (obj is null)
    {
      return;
    }
    ArgumentNullException.ThrowIfNull(filepath);

    using var fs = new FileStream(filepath, new FileStreamOptions
    {
      Access = FileAccess.Write,
      Mode = FileMode.Create,
      Share = FileShare.None,
      Options = FileOptions.Asynchronous,
    });
    using var utf8js = new Utf8JsonWriter(fs, new JsonWriterOptions
    {
      Indented = true,
      Encoder = asciiEscaping ? JavaScriptEncoder.Default : JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
    JsonSerializer.Serialize(utf8js, obj, new JsonSerializerOptions
    {
      WriteIndented = true,
      Encoder = asciiEscaping ? JavaScriptEncoder.Default : JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
      NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    });
  }

  public static T? LoadJson<T>(string filepath)
  {
    ArgumentNullException.ThrowIfNull(filepath);

    using var fs = new FileStream(filepath, new FileStreamOptions
    {
      Access = FileAccess.Read,
      Mode = FileMode.Open,
      Share = FileShare.Read,
    });
    var obj = JsonSerializer.Deserialize<T>(fs, new JsonSerializerOptions
    {
      AllowTrailingCommas = true,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      ReadCommentHandling = JsonCommentHandling.Skip,
      NumberHandling =
        System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
      PreferredObjectCreationHandling =
        System.Text.Json.Serialization.JsonObjectCreationHandling.Populate,

    });
    return obj;
  }

  public static ulong GetUnixEpoch()
  {
    return (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();
  }

}

