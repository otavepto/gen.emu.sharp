using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading.Tasks;
using System.Globalization;


namespace gen.emu.shared;

public static class Helpers
{

  public static void PrintSteamKitKeyValue(SteamKit2.KeyValue kv, string spaces = "")
  {
    Console.WriteLine($"{spaces}'{kv.Name}' (children={kv.Children.Count}) = '{kv.Value}'");
    foreach (var item in kv.Children)
    {
      PrintSteamKitKeyValue(item, "  " + spaces);
    }
  }

  public static void PrintVdfKeyValue(ValveKeyValue.KVObject kv, string spaces = "")
  {
    Console.Write($"{spaces}'{kv.Name}' (children={kv.Children.Count()}): <{kv.Value.ValueType}>");
    if (kv.Children.Any())
    {
      Console.WriteLine();
      foreach (var item in kv.Children)
      {
        PrintVdfKeyValue(item, "  " + spaces);
      }
    }
    else
    {
      Console.WriteLine($" = '{kv.Value}'");
    }

  }

  public static JsonObject ToJsonObj(this SteamKit2.KeyValue? steamKitKeyValue)
  {
    if (steamKitKeyValue is null)
    {
      return new();
    }

    JsonObject rootJobj = new();
    Queue<(SteamKit2.KeyValue kvPair, JsonObject myJobj)> pending = new([
      (steamKitKeyValue, rootJobj)
    ]);

    while (pending.Count > 0)
    {
      var (kv, currentObj) = pending.Dequeue();
      string nameSafe = kv.Name is null ? string.Empty : kv.Name;
      if (kv.Children.Count == 0) // regular "key" : "value"
      {
        if (currentObj.TryGetPropertyValue(nameSafe, out var oldVal)) // name exists
        {
          if (oldVal is null) // convert it to array
          {
            /* "some_prop": null
             * 
             * >>>
             * 
             * "some_prop": [
             *  null,
             *  <new value here>
             * ]
             */
            currentObj.Remove(nameSafe);
            currentObj[nameSafe] = new JsonArray(null, kv.Value);
          }
          else if (oldVal.GetValueKind() == JsonValueKind.Array) // previously converted
          {
            oldVal.AsArray().Add(kv.Value);
          }
          else // convert it to array
          {
            /* "some_prop": "old value"
             * 
             * >>>
             * 
             * "some_prop": [
             *  "old value",
             *  <new value here>
             * ]
             */
            currentObj.Remove(nameSafe);
            currentObj[nameSafe] = new JsonArray(oldVal, kv.Value);
          }
        }
        else // new name
        {
          currentObj[nameSafe] = kv.Value;
        }
      }
      else // nested object "key" : { ... }
      {
        JsonObject newObj = new(); // new container for the key/value pairs

        if (currentObj.TryGetPropertyValue(nameSafe, out var oldNode) && oldNode is not null)
        {
          // if key already exists then convert the parent container to array of objects
          /*
           * "controller_mappings": {
           *  "group": {},                // ===== 1
           * }
           * 
           * >>>
           * 
           * "controller_mappings": {
           *  "group": [                  // ==== 2
           *    {},
           *    {},
           *    {},
           *  ]
           * }
           * 
           */
          if (oldNode.GetValueKind() == JsonValueKind.Object) // ===== 1
          {
            // convert it to array
            currentObj.Remove(nameSafe);
            currentObj[nameSafe] = new JsonArray(oldNode, newObj); // ==== 2
          }
          else // already converted to array
          {
            oldNode.AsArray().Add(newObj);
          }
        }
        else // entirely new key, start as an object/dictionary
        {
          currentObj[nameSafe] = newObj;
        }

        // add all nested elements for the next iterations
        foreach (var item in kv.Children)
        {
          // the owner of element will be this new json object
          pending.Enqueue((item, newObj));
        }

      }
    }

    return rootJobj;
  }

  public static JsonObject ToJsonObj(this ValveKeyValue.KVObject? vdfKeyValue)
  {
    if (vdfKeyValue is null)
    {
      return new();
    }

    JsonObject rootJobj = new();
    Queue<(ValveKeyValue.KVObject kvPair, JsonObject myJobj)> pending = new([
      (vdfKeyValue, rootJobj)
    ]);

    static JsonNode? SingleVdfKvToJobj(ValveKeyValue.KVValue val)
    {
      switch (val.ValueType)
      {
        case ValveKeyValue.KVValueType.Null:
          return null;
        case ValveKeyValue.KVValueType.Collection:
          return JsonNode.Parse("{}");
        case ValveKeyValue.KVValueType.Array:
          return JsonNode.Parse("[]");
        case ValveKeyValue.KVValueType.BinaryBlob:
          return JsonNode.Parse("[]");
        case ValveKeyValue.KVValueType.String:
          return (string?)val ?? string.Empty;
        case ValveKeyValue.KVValueType.Int32:
          return (int)val;
        case ValveKeyValue.KVValueType.UInt64:
          return (ulong)val;
        case ValveKeyValue.KVValueType.FloatingPoint:
          return (double)val;
        case ValveKeyValue.KVValueType.Pointer:
          return (ulong)val;
        case ValveKeyValue.KVValueType.Int64:
          return (long)val;
        case ValveKeyValue.KVValueType.Boolean:
          return (bool)val;
        default:
          return val.ToString();
      }

    }

    while (pending.Count > 0)
    {
      var (kv, currentObj) = pending.Dequeue();
      string nameSafe = kv.Name is null ? string.Empty : kv.Name;
      if (!kv.Children.Any()) // regular "key" : "value"
      {
        if (currentObj.TryGetPropertyValue(nameSafe, out var oldVal)) // name exists
        {
          if (oldVal is null) // convert it to array
          {
            /* "some_prop": null
             * 
             * >>>
             * 
             * "some_prop": [
             *  null,
             *  <new value here>
             * ]
             */
            currentObj.Remove(nameSafe);
            currentObj[nameSafe] = new JsonArray(null, SingleVdfKvToJobj(kv.Value));
          }
          else if (oldVal.GetValueKind() == JsonValueKind.Array) // previously converted
          {
            oldVal.AsArray().Add(kv.Value);
          }
          else // convert it to array
          {
            /* "some_prop": "old value"
             * 
             * >>>
             * 
             * "some_prop": [
             *  "old value",
             *  <new value here>
             * ]
             */
            currentObj.Remove(nameSafe);
            currentObj[nameSafe] = new JsonArray(oldVal, SingleVdfKvToJobj(kv.Value));
          }
        }
        else // new name
        {
          currentObj[nameSafe] = SingleVdfKvToJobj(kv.Value);
        }
      }
      else // nested object "key" : { ... }
      {
        JsonObject newObj = new(); // new container for the key/value pairs

        if (currentObj.TryGetPropertyValue(nameSafe, out var oldNode) && oldNode is not null)
        {
          // if key already exists then convert the parent container to array of objects
          /*
           * "controller_mappings": {
           *  "group": {},                // ===== 1
           * }
           * 
           * >>>
           * 
           * "controller_mappings": {
           *  "group": [                  // ==== 2
           *    {},
           *    {},
           *    {},
           *  ]
           * }
           * 
           */
          if (oldNode.GetValueKind() == JsonValueKind.Object) // ===== 1
          {
            // convert it to array
            currentObj.Remove(nameSafe);
            currentObj[nameSafe] = new JsonArray(oldNode, newObj); // ==== 2
          }
          else // already converted to array
          {
            oldNode.AsArray().Add(newObj);
          }
        }
        else // entirely new key, start as an object/dictionary
        {
          currentObj[nameSafe] = newObj;
        }

        // add all nested elements for the next iterations
        foreach (var item in kv.Children)
        {
          // the owner of element will be this new json object
          pending.Enqueue((item, newObj));
        }

      }
    }

    return rootJobj;
  }

  public static JsonArray ToVdfArraySafe(this JsonNode? node)
  {
    if (node is null)
    {
      return [];
    }

    switch (node.GetValueKind())
    {
      case JsonValueKind.Array:
        return node.AsArray();
    }

    return [node.DeepClone()];
  }

  public static JsonObject CreateVdfObj(Stream textStream)
  {
    ArgumentNullException.ThrowIfNull(textStream);

    var kv = ValveKeyValue.KVSerializer.Create(ValveKeyValue.KVSerializationFormat.KeyValues1Text);
    var vdfDataDoc = kv.Deserialize(textStream, new ValveKeyValue.KVSerializerOptions
    {
      EnableValveNullByteBugBehavior = true,
    });

    return ToJsonObj(vdfDataDoc);
  }

}

