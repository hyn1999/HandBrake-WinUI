namespace HandBrake.Interop.Interop
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;

    // Read the native dictionary directly: hb_value_get_json allocates with the
    // native CRT, so its buffer must not be released with Marshal.FreeHGlobal.
    internal static class HandBrakePresetJob
    {
        internal static string BuiltInPresets()
        {
            IntPtr presets = hb_presets_builtin_get();
            if (presets == IntPtr.Zero) throw new InvalidOperationException("HandBrake returned no built-in presets.");
            // hb_presets_builtin_get returns a deep copy, owned by the caller.
            try { return Serialize(presets); }
            finally { hb_value_free(ref presets); }
        }

        private static string Serialize(IntPtr value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) WriteValue(writer, value, 0);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        internal static string Create(IntPtr handle, int titleIndex, string presetJson)
        {
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Initialize and scan the HandBrake instance first.");
            if (titleIndex < 1) throw new ArgumentOutOfRangeException(nameof(titleIndex));
            using (var document = JsonDocument.Parse(presetJson))
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("A preset must be a JSON object.", nameof(presetJson));
            IntPtr input = IntPtr.Zero, preset = IntPtr.Zero, job = IntPtr.Zero;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(presetJson + "\0");
                input = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, input, bytes.Length);
                preset = hb_value_json(input);
                if (preset == IntPtr.Zero) throw new InvalidOperationException("HandBrake rejected the preset JSON.");
                job = hb_preset_job_init(handle, titleIndex, preset);
                if (job == IntPtr.Zero) throw new InvalidOperationException("HandBrake could not apply the preset to the scanned title. Check the native log.");
                return Serialize(job);
            }
            finally
            {
                if (job != IntPtr.Zero) hb_value_free(ref job);
                if (preset != IntPtr.Zero) hb_value_free(ref preset);
                if (input != IntPtr.Zero) Marshal.FreeHGlobal(input);
            }
        }

        private static void WriteValue(Utf8JsonWriter writer, IntPtr value, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("The native job exceeds the JSON depth limit.");
            if (value == IntPtr.Zero) { writer.WriteNullValue(); return; }
            // hb_dict.h: object=0, array=1, string=2, integer=3, real=4,
            // null=7; hb_value_type maps both boolean types to 0xff.
            switch (hb_value_type(value))
            {
                case 0:
                    writer.WriteStartObject();
                    for (IntPtr iter = hb_dict_iter_init(value); iter != IntPtr.Zero; iter = hb_dict_iter_next(value, iter))
                    {
                        writer.WritePropertyName(Marshal.PtrToStringUTF8(hb_dict_iter_key(iter)) ?? throw new InvalidOperationException("Native job has an invalid key."));
                        WriteValue(writer, hb_dict_iter_value(iter), depth + 1);
                    }
                    writer.WriteEndObject();
                    break;
                case 1:
                    writer.WriteStartArray();
                    ulong count = hb_value_array_len(value).ToUInt64();
                    if (count > int.MaxValue) throw new InvalidOperationException("The native job array is too large.");
                    for (int index = 0; index < (int)count; index++) WriteValue(writer, hb_value_array_get(value, index), depth + 1);
                    writer.WriteEndArray();
                    break;
                case 2: writer.WriteStringValue(Marshal.PtrToStringUTF8(hb_value_get_string(value))); break;
                case 3: writer.WriteNumberValue(hb_value_get_int(value)); break;
                case 4: writer.WriteNumberValue(hb_value_get_double(value)); break;
                case 0xff: writer.WriteBooleanValue(hb_value_get_bool(value) != 0); break;
                case 7: writer.WriteNullValue(); break;
                default: throw new InvalidOperationException("HandBrake returned an unsupported JSON value.");
            }
        }

        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_presets_builtin_get();
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_value_json(IntPtr json);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_preset_job_init(IntPtr handle, int titleIndex, IntPtr preset);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern void hb_value_free(ref IntPtr value);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern int hb_value_type(IntPtr value);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_dict_iter_init(IntPtr value);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_dict_iter_next(IntPtr value, IntPtr iter);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_dict_iter_key(IntPtr iter);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_dict_iter_value(IntPtr iter);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern UIntPtr hb_value_array_len(IntPtr value);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_value_array_get(IntPtr value, int index);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr hb_value_get_string(IntPtr value);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern long hb_value_get_int(IntPtr value);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern double hb_value_get_double(IntPtr value);
        [DllImport("hb", CallingConvention = CallingConvention.Cdecl)] private static extern int hb_value_get_bool(IntPtr value);
    }
}
