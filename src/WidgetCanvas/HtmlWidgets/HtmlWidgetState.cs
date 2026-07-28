#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace WidgetCanvas.HtmlWidgets
{
    internal static class HtmlWidgetState
    {
        public const int MaximumKeyLength = 256;
        public const int MaximumEntryCount = 2048;
        public const int MaximumUtf8Bytes = 4 * 1024 * 1024;

        public static void Set(
            Dictionary<string, JsonElement> state,
            string key,
            JsonElement value)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentException.ThrowIfNullOrEmpty(key);
            if (key.Length > MaximumKeyLength)
                throw new ArgumentException($"状态键不能超过 {MaximumKeyLength} 个字符", nameof(key));
            if (value.ValueKind == JsonValueKind.Undefined)
                throw new ArgumentException("状态值必须是可序列化的 JSON", nameof(value));
            if (!state.ContainsKey(key) && state.Count >= MaximumEntryCount)
                throw new InvalidOperationException($"每个组件最多保存 {MaximumEntryCount} 个状态项");

            long totalBytes = 2;
            bool replaced = false;
            foreach (KeyValuePair<string, JsonElement> item in state)
            {
                JsonElement itemValue;
                if (string.Equals(item.Key, key, StringComparison.Ordinal))
                {
                    itemValue = value;
                    replaced = true;
                }
                else
                {
                    itemValue = item.Value;
                }

                totalBytes += GetEntryUtf8Bytes(item.Key, itemValue);
                if (totalBytes > MaximumUtf8Bytes)
                    throw CreateSizeException();
            }

            if (!replaced)
            {
                totalBytes += GetEntryUtf8Bytes(key, value);
                if (totalBytes > MaximumUtf8Bytes)
                    throw CreateSizeException();
            }

            state[key] = value.Clone();
        }

        private static long GetEntryUtf8Bytes(string key, JsonElement value)
        {
            int encodedKeyBytes = JsonEncodedText.Encode(key).EncodedUtf8Bytes.Length;
            int valueBytes = Encoding.UTF8.GetByteCount(value.GetRawText());
            // 两个引号、冒号和最多一个逗号。略高估 1 字节可让边界保持简单。
            return encodedKeyBytes + valueBytes + 4L;
        }

        private static InvalidOperationException CreateSizeException() =>
            new($"每个组件最多保存 {MaximumUtf8Bytes / (1024 * 1024)} MiB 状态数据");
    }
}
