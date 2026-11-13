using System;
using ChromaGLS.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChromaGLS.HarmonyPatches
{
    // Streams tokens from the wrapped reader until a node object position is reached, buffers that one
    // JObject, applies its customData.easing override to the OEM field, then replays the node through
    // the base token state so the original reader sees an ordinary stream.
    internal sealed class NodeEasingReader : JsonReader
    {
        private readonly JsonReader _source;
        private readonly bool _fxCollection;
        private readonly string _easingKey;
        private JsonReader? _replay;
        private int _containerDepth;
        private int _rootDepth = -1;
        private bool _insideFloatEvents;

        public NodeEasingReader(JsonReader source, bool fxCollection)
        {
            _source = source;
            _fxCollection = fxCollection;
            _easingKey = fxCollection ? "i" : "e";
            _containerDepth = source.Depth;
            if (source.TokenType != JsonToken.None)
            {
                SetToken(source.TokenType, source.Value);
            }
        }

        // Only the buffered replay is disposable; the wrapped reader belongs to the map parser.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (_replay as IDisposable)?.Dispose();
            }
        }

        public override bool Read()
        {
            if (_replay != null)
            {
                if (_replay.Read())
                {
                    SetToken(_replay.TokenType, _replay.Value);
                    TrackEmitted(_replay.TokenType);
                    return true;
                }

                ((IDisposable)_replay).Dispose();
                _replay = null;
            }

            return ReadSource();
        }

        private bool ReadSource()
        {
            if (!_source.Read())
            {
                SetToken(JsonToken.None);
                return false;
            }

            JsonToken token = _source.TokenType;
            if (token == JsonToken.PropertyName && _fxCollection
                && _containerDepth == _rootDepth
                && _source.Value as string == "_fl")
            {
                _insideFloatEvents = true;
            }

            if (token == JsonToken.StartObject && IsNodeObject())
            {
                // Positioned on the node's '{'; JObject.Load consumes it through the matching '}'.
                JObject node = JObject.Load(_source);
                ApplyEasingOverride(node);
                _replay = node.CreateReader();
                SetToken(JsonToken.None);
                return Read();
            }

            SetToken(token, _source.Value);
            TrackEmitted(token);
            return true;
        }

        private void TrackEmitted(JsonToken token)
        {
            switch (token)
            {
                case JsonToken.StartObject:
                case JsonToken.StartArray:
                case JsonToken.StartConstructor:
                    _containerDepth++;
                    if (_rootDepth < 0)
                    {
                        _rootDepth = _containerDepth;
                    }

                    break;

                case JsonToken.EndObject:
                case JsonToken.EndArray:
                case JsonToken.EndConstructor:
                    if (_fxCollection && _insideFloatEvents && _containerDepth == _rootDepth + 1)
                    {
                        _insideFloatEvents = false;
                    }

                    _containerDepth--;
                    break;
            }
        }

        private bool IsNodeObject()
        {
            if (_rootDepth < 0)
            {
                // The node stream's outermost container opens first; remember it for relative depths.
                return false;
            }

            // Rotation/translation event objects sit inside group > box > l arrays; FX nodes sit
            // directly inside the fxEventsCollection _fl array.
            int nodeDepth = _fxCollection ? _rootDepth + 2 : _rootDepth + 5;
            if (_fxCollection)
            {
                return _insideFloatEvents && _containerDepth + 1 == nodeDepth;
            }

            return _containerDepth + 1 == nodeDepth;
        }

        private void ApplyEasingOverride(JObject node)
        {
            if (node["customData"] is not JObject custom
                || custom["easing"] is not JValue { Type: not JTokenType.Null } raw
                || !GlsEasingRuntimePatch.TryParseEasingId(raw.Value, out int id)
                || !(GlsEasingRuntimePatch.IsSupportedCurveId(id) || GlsSplineMath.IsCatmullRom(id)))
            {
                return;
            }

            node[_easingKey] = id;
        }
    }
}
