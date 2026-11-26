// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace VDF.Core.AI {
	/// <summary>
	/// Fallback source for the ONNX Runtime native libraries. The regular download is the
	/// onnxruntime release archive on GitHub; when that cannot be fetched (the release page
	/// answered 404 for every user at one point, which silently took AI matching away) the same
	/// release's PyPI wheel is used instead: a wheel is a zip whose onnxruntime/capi folder
	/// holds the very same C libraries (onnxruntime.dll, libonnxruntime.so.&lt;ver&gt;,
	/// libonnxruntime.&lt;ver&gt;.dylib). Every wheel is pinned by SHA256.
	/// </summary>
	internal static class AiRuntimeWheel {
		/// <summary>The onnxruntime release the pinned wheels below belong to.</summary>
		internal const string WheelVersion = "1.23.2";

		internal readonly record struct Plan(Uri Url, string FileName, string Sha256);

		/// <summary>
		/// The wheel for this machine, or null when there is none (Windows on ARM has no wheel) or
		/// when <paramref name="runtimeVersion"/> has moved on without these pins being updated.
		/// </summary>
		internal static Plan? GetPlan(string runtimeVersion) {
			OSPlatform os =
				RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows :
				RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX :
				OSPlatform.Linux;
			return GetPlan(runtimeVersion, os, RuntimeInformation.ProcessArchitecture);
		}

		internal static Plan? GetPlan(string runtimeVersion, OSPlatform os, Architecture arch) {
			if (runtimeVersion != WheelVersion) return null;

			if (os == OSPlatform.Windows && arch == Architecture.X64)
				return Make("c0/b4/569d298f9fc4d286c11c45e85d9ffa9e877af12ace98af8cab52396e8f46", "onnxruntime-1.23.2-cp312-cp312-win_amd64.whl",
					"25de5214923ce941a3523739d34a520aac30f21e631de53bba9174dc9c004435");
			if (os == OSPlatform.Linux && arch == Architecture.X64)
				return Make("ef/88/9cc25d2bafe6bc0d4d3c1db3ade98196d5b355c0b273e6a5dc09c5d5d0d5", "onnxruntime-1.23.2-cp312-cp312-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
					"0f9b4ae77f8e3c9bee50c27bc1beede83f786fe1d52e99ac85aa8d65a01e9b77");
			if (os == OSPlatform.Linux && arch == Architecture.Arm64)
				return Make("3c/35/4e40f2fba272a6698d62be2cd21ddc3675edfc1a4b9ddefcc4648f115315", "onnxruntime-1.23.2-cp312-cp312-manylinux_2_27_aarch64.manylinux_2_28_aarch64.whl",
					"76ff670550dc23e58ea9bc53b5149b99a44e63b34b524f7b8547469aaa0dcb8c");
			if (os == OSPlatform.OSX && arch == Architecture.Arm64)
				return Make("1b/9e/f748cd64161213adeef83d0cb16cb8ace1e62fa501033acdd9f9341fff57", "onnxruntime-1.23.2-cp312-cp312-macosx_13_0_arm64.whl",
					"b8f029a6b98d3cf5be564d52802bb50a8489ab73409fa9db0bf583eabb7c2321");
			if (os == OSPlatform.OSX && arch == Architecture.X64)
				return Make("91/9d/a81aafd899b900101988ead7fb14974c8a58695338ab6a0f3d6b0100f30b", "onnxruntime-1.23.2-cp312-cp312-macosx_13_0_x86_64.whl",
					"218295a8acae83905f6f1aed8cacb8e3eb3bd7513a13fe4ba3b2664a19fc4a6b");
			return null;
		}

		static Plan Make(string packagePath, string fileName, string sha256) =>
			new(new Uri($"https://files.pythonhosted.org/packages/{packagePath}/{fileName}"), fileName, sha256);

		/// <summary>
		/// True for the native runtime libraries inside the wheel. The Python extension module
		/// (onnxruntime_pybind11_state.pyd / .so) also has "onnxruntime" in its name but is not
		/// the C library and must never be picked as the runtime.
		/// </summary>
		internal static bool IsNativeRuntimeFile(string fileName) {
			if (!fileName.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase)) return false;
			if (fileName.Contains("pybind", StringComparison.OrdinalIgnoreCase)) return false;
			return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
				|| fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
				|| fileName.Contains(".so", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The native library files of an extracted wheel (its onnxruntime/capi folder).</summary>
		internal static string[] FindLibraries(string extractedFolder) {
			string? capi = Directory.EnumerateDirectories(extractedFolder, "capi", SearchOption.AllDirectories).FirstOrDefault();
			if (capi == null)
				throw new IOException("The ONNX Runtime wheel has no onnxruntime/capi folder.");
			string[] libraries = Directory.EnumerateFiles(capi).Where(f => IsNativeRuntimeFile(Path.GetFileName(f))).ToArray();
			if (libraries.Length == 0)
				throw new IOException("The ONNX Runtime wheel contains no native runtime library.");
			return libraries;
		}

		internal static void VerifySha256(string path, string expectedHex) {
			using FileStream stream = File.OpenRead(path);
			string actual = Convert.ToHexString(SHA256.HashData(stream));
			if (!actual.Equals(expectedHex, StringComparison.OrdinalIgnoreCase))
				throw new IOException($"The ONNX Runtime download failed the integrity check (SHA256 {actual}, expected {expectedHex}).");
		}
	}
}
