# Video presentation and WPF behavior alignment

## Layout

Video settings and Quality are separate native-control groups, side by side when space permits and stacked when narrow. Encoder Options follows in a shared-label grid. Encoder Tune/Fast Decode and Profile/Level stay adjacent and wrap as related groups. Advanced Options is a native multiline TextBox with a shared minimum-height resource. Removed duplicate source codec/container details, Preset reference, success/Ready messages, and implementation prose. Actual unavailable-encoder, incompatible-container and invalid-mode/bitrate errors remain visible only when applicable.

Constant Quality and Average Bitrate use native RadioButtons; unsupported choices are disabled/hidden according to native capability. The selected rate-control state is an explicit private field, not a hidden ComboBox. CFR/VFR/PFR also use native radio controls and an explicit native mode string. Encoder Preset deliberately remains a bounded native ComboBox, preserving the native-default entry and all discrete values without guessing a default slider position. Its tooltip retains the first two verbatim WPF descriptive paragraphs and omits only the paragraph giving WPF-slider interaction instructions. Other tooltips are read in full from WPF through WpfText.

## Behavior

- Quality limits, granularity, direction, adjustment support and units come from the matching native encoder. The slider uses IsDirectionReversed when smaller native numbers mean higher quality, leaving the stored RF/QP/CQ value unchanged. The right endpoint therefore means higher quality in both ascending and descending native scales. Native lossless/fixed-quality encoders show their WPF lossless description instead of an adjustable slider and clamp to the native singleton range.
- User changes to an explicit framerate convert VFR to PFR; switching PFR to Same as source converts it back to VFR. CFR is retained. A loaded explicit-rate VFR snapshot is preserved, with a visible VFR choice, until the user changes it. Unknown mode strings are rejected, and valid custom numeric framerates are kept rather than silently changed to Same as source.
- Color range now includes the original Same as source/auto choice as well as Limited and Full; loaded auto values are retained.
- Multi-pass availability uses native capability for the selected quality/bitrate mode. Turbo additionally requires ABR, enabled multi-pass and an x264/x265 encoder. Unsupported pass/Fast Decode controls collapse. Speed/profile/level/tune values remain sourced from native encoder metadata, and private settings reset on encoder changes as before.
- Existing native preset capture, filter patches, queue schema and native job resolution remain in place. TryGetVideoDraftSettings retains the same capture logic, reading the radio-backed rate mode instead of the old ComboBox. Existing queue-edit native-job equality validation is unchanged.

## Sources and validation

Reviewed WPF VideoView.xaml, VideoViewModel.SelectedFramerate/quality mapping and HighQualityLabel, original WPF label/help catalogs, HBVideoEncoder, VideoQualityLimits and HandBrakeEncoderHelpers at ca1dbf2143ff53264a2377db5da88449b1ac117d. VideoColorRange=auto was checked against libhb/preset.c. Product captions and tooltip keys are read from the original catalogs rather than paraphrased. No native ControlTemplate or command-strip style was replaced.

Performed static XML, resource, x:Bind-property, name and event-handler checks; checked removed control references, WPF resource keys and programmatic control ownership. Compared video-draft capture against its prior source after substituting the mode state reference. No .NET SDK or Windows runtime is available locally. No compilation, native execution, Windows UI test or build was performed.

Next aggregate Windows acceptance: narrow/wide layouts and enlarged text; radio keyboard/focus order; ascending and descending quality scales, negative ranges and fractional steps; RF/QP/CQ labels and FFV1/fixed quality; CQ/ABR switching and invalid bitrate; all CFR/VFR/PFR transitions including saved explicit VFR and custom rates; color range auto; native/hardware and bitrate-only encoders; multi-pass/turbo gating; encoder switches and discrete defaults; Modified/Reload; new and existing queue snapshots and queue-edit native-job equality; Summary and preview consistency; multiline options scrolling, themes and WPF tooltip behavior. Settings-tab redesign remains deferred.
