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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VDF.GUI.ViewModels {
	/// <summary>
	/// The results search box (and the filter panel, which writes the same terms) understands
	/// "field operator value" terms next to plain words. Terms are separated by spaces; wrap a
	/// value in double quotes to keep spaces in it.
	///
	///   name:text     file name (without folders) contains text; several words ("first second")
	///                 must all appear, in any order
	///   name=text     file name is exactly text (with or without the extension)
	///   name!=text    no file of the group is named exactly text
	///   name!:text    no file of the group has a name containing text
	///   path:text     full path contains text (same for path=, path!=, path!:)
	///   ext:mkv,mp4   file extension is one of the listed ones (ext!: excludes them)
	///   size>500MB    file size (also >=, &lt;, &lt;=, =); units B, KB, MB, GB, TB; no unit = MB
	///   duration>=1h30m   play time: 90 (minutes), 1h30m, 45m, 30s, 1:30:00
	///   res>=1080     resolution of the shorter side: 1080, 1080p, 1920x1080, 4k
	///   bitrate>=5000 video bitrate: 5000 (kbps), 5mbps
	///   fps>=24       frames per second
	///   created>=2024-01-31   file creation date (also modified); yyyy-MM-dd or dd/MM/yyyy,
	///                         optionally with a time in quotes: created>="2024-01-31 14:30"
	///   total&lt;10GB     all files of the group together
	///   count>=3      number of files in the group
	///
	/// "*" and "?" work as wildcards inside name and path values.
	/// Plain words (no field) must appear in the path, like before.
	///
	/// How terms combine: every positive file term (words, name:, path:, name=, ext:, size,
	/// duration, res, bitrate, fps, created, modified) has to be satisfied by the SAME file,
	/// and a group is shown when at least one of its files satisfies all of them. Negative
	/// terms (!= and !:) exclude a group as soon as any of its files matches the positive
	/// form. total and count apply to the whole group.
	/// </summary>
	internal sealed class ResultsSearchQuery {
		/// <summary>What the query needs to know about one file.</summary>
		internal readonly record struct FileRecord(
			string Path, long Size, double DurationSeconds, double ShortSide,
			double BitrateKbps, double Fps, double CreatedTicks, double ModifiedTicks);

		enum Field { Path, Name, Ext, Size, Total, Duration, Res, Bitrate, Fps, Created, Modified, Count }
		enum Op { Contains, NotContains, Exact, NotExact, Greater, GreaterOrEqual, Less, LessOrEqual }

		sealed class Term {
			public Field Field;
			public Op Op;
			public string Text = string.Empty;
			/// <summary>name:/path: contains-terms with several words: all of them must appear, in any order.</summary>
			public string[]? Words;
			public double Number;
			public string[] Extensions = Array.Empty<string>();
		}

		static readonly Regex FieldTermRegex = new(
			@"^(path|name|ext|size|total|duration|res|bitrate|fps|created|modified|count)(!=|!:|>=|<=|=|:|>|<)(.*)$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

		static readonly Regex SizeRegex = new(
			@"^\s*(\d+(?:[.,]\d+)?)\s*([a-z]*)\s*$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		static readonly Regex NumberRegex = new(@"^\d+(?:[.,]\d+)?$", RegexOptions.CultureInvariant);

		static readonly Regex DurationPartRegex = new(
			@"(\d+(?:[.,]\d+)?)\s*(hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		static readonly Regex BitrateRegex = new(
			@"^\s*(\d+(?:[.,]\d+)?)\s*(kbps|kb|k|mbps|mb|m)?\s*$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		static readonly Regex ResolutionRegex = new(
			@"^\s*(\d+)\s*[xX\u00D7]\s*(\d+)\s*$|^\s*(\d+)\s*[pP]?\s*$",
			RegexOptions.CultureInvariant);

		static readonly string[] DateOnlyFormats = {
			"yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyy-M-d", "yyyy/M/d",
			"dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy" };

		static readonly string[] DateTimeFormats = {
			"yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss",
			"yyyy/MM/dd HH:mm", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss" };

		/// <summary>Terms one file must satisfy all at once.</summary>
		readonly List<Term> fileTerms = new();
		/// <summary>Terms (in their positive form) no file of a shown group may satisfy.</summary>
		readonly List<Term> excludeTerms = new();
		/// <summary>Terms about the whole group: total size and number of files.</summary>
		readonly List<Term> groupTerms = new();

		/// <summary>
		/// Returns null when the text uses none of the extra syntax (no field term, no quotes);
		/// the caller then keeps the classic path search for it.
		/// </summary>
		internal static ResultsSearchQuery? Parse(string? text) {
			if (string.IsNullOrWhiteSpace(text)) return null;

			bool special = text.Contains('"');
			var query = new ResultsSearchQuery();
			foreach (string token in SplitTokens(text)) {
				if (TryParseFieldTerm(token, out Term? term)) {
					special = true;
					if (term == null) continue; // value still empty while typing: ignore for now
					if (term.Field is Field.Total or Field.Count)
						query.groupTerms.Add(term);
					else if (term.Op is Op.NotContains or Op.NotExact)
						query.excludeTerms.Add(term);
					else
						query.fileTerms.Add(term);
				}
				else {
					query.fileTerms.Add(new Term { Field = Field.Path, Op = Op.Contains, Text = token });
				}
			}
			return special ? query : null;
		}

		/// <summary>True when the text is exactly one complete, valid field term (used by the filter panel).</summary>
		internal static bool IsValidTerm(string text) {
			var tokens = SplitTokens(text);
			return tokens.Count == 1 && TryParseFieldTerm(tokens[0], out Term? term) && term != null;
		}

		/// <summary>Splits on whitespace; text inside double quotes stays together (the quotes are dropped).</summary>
		internal static List<string> SplitTokens(string text) {
			var tokens = new List<string>();
			var current = new StringBuilder();
			bool inQuotes = false;
			foreach (char c in text) {
				if (c == '"') { inQuotes = !inQuotes; continue; }
				if (!inQuotes && char.IsWhiteSpace(c)) {
					if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
					continue;
				}
				current.Append(c);
			}
			if (current.Length > 0) tokens.Add(current.ToString());
			return tokens;
		}

		/// <summary>
		/// False: the token is not a valid field term (it is treated as a plain word instead).
		/// True with a null term: a field term whose value is still empty.
		/// </summary>
		static bool TryParseFieldTerm(string token, out Term? term) {
			term = null;
			var m = FieldTermRegex.Match(token);
			if (!m.Success) return false;

			Field field = ParseField(m.Groups[1].Value);
			string op = m.Groups[2].Value;
			string value = m.Groups[3].Value.Trim();

			if (value.Length == 0) return true;

			Op? parsedOp = ParseOp(op, field);
			if (parsedOp == null) return false;

			var t = new Term { Field = field, Op = parsedOp.Value };
			switch (field) {
				case Field.Path:
				case Field.Name:
					t.Text = value;
					if ((t.Op is Op.Contains or Op.NotContains) && value.IndexOfAny(new[] { '*', '?' }) < 0) {
						string[] words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
						if (words.Length > 1) t.Words = words;
					}
					break;
				case Field.Ext:
					t.Extensions = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
						.Select(e => e.Trim().TrimStart('.'))
						.Where(e => e.Length > 0)
						.ToArray();
					if (t.Extensions.Length == 0) return true;
					break;
				case Field.Size:
				case Field.Total:
					if (!TryParseSize(value, out long bytes)) return false;
					t.Number = bytes;
					break;
				case Field.Duration:
					if (!TryParseDuration(value, out double seconds)) return false;
					t.Number = seconds;
					break;
				case Field.Res:
					if (!TryParseResolution(value, out double side)) return false;
					t.Number = side;
					break;
				case Field.Bitrate:
					if (!TryParseBitrate(value, out double kbps)) return false;
					t.Number = kbps;
					break;
				case Field.Fps:
					if (!TryParseNumber(value, out double fps)) return false;
					t.Number = fps;
					break;
				case Field.Count:
					if (!NumberRegex.IsMatch(value) || value.IndexOfAny(new[] { '.', ',' }) >= 0) return false;
					t.Number = double.Parse(value, CultureInfo.InvariantCulture);
					break;
				default: // Created, Modified
					if (!TryParseDate(value, out DateTime date, out bool dateOnly)) return false;
					if (dateOnly) {
						// A bare date means the whole day: "<=2024-05-01" includes the 1st at any time.
						if (t.Op == Op.Greater) { t.Op = Op.GreaterOrEqual; date = date.AddDays(1); }
						else if (t.Op == Op.LessOrEqual) { t.Op = Op.Less; date = date.AddDays(1); }
					}
					t.Number = date.Ticks;
					break;
			}
			term = t;
			return true;
		}

		static Field ParseField(string name) {
			switch (name.ToLowerInvariant()) {
				case "path": return Field.Path;
				case "name": return Field.Name;
				case "ext": return Field.Ext;
				case "size": return Field.Size;
				case "total": return Field.Total;
				case "duration": return Field.Duration;
				case "res": return Field.Res;
				case "bitrate": return Field.Bitrate;
				case "fps": return Field.Fps;
				case "created": return Field.Created;
				case "modified": return Field.Modified;
				default: return Field.Count;
			}
		}

		static Op? ParseOp(string op, Field field) {
			if (field is Field.Path or Field.Name or Field.Ext) {
				switch (op) {
					case ":": return Op.Contains;
					case "!:": return Op.NotContains;
					case "=": return Op.Exact;
					case "!=": return Op.NotExact;
					default: return null;
				}
			}
			bool isDate = field is Field.Created or Field.Modified;
			switch (op) {
				case "=": return isDate ? null : Op.Exact;
				case ">": return Op.Greater;
				case ">=": return Op.GreaterOrEqual;
				case "<": return Op.Less;
				case "<=": return Op.LessOrEqual;
				default: return null;
			}
		}

		/// <summary>"3", "2.5", "2,5": a plain non-negative number.</summary>
		internal static bool TryParseNumber(string text, out double number) {
			number = 0;
			text = text.Trim();
			if (!NumberRegex.IsMatch(text)) return false;
			return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}

		/// <summary>"500", "1.5GB", "1,5 gb", "512kb": a number plus optional B/KB/MB/GB/TB. No unit means MB.</summary>
		internal static bool TryParseSize(string text, out long bytes) {
			bytes = 0;
			var m = SizeRegex.Match(text);
			if (!m.Success) return false;
			if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
				return false;

			double factor;
			switch (m.Groups[2].Value.ToLowerInvariant()) {
				case "b": factor = 1; break;
				case "k": case "kb": case "kib": factor = 1024d; break;
				case "": case "m": case "mb": case "mib": factor = 1024d * 1024d; break;
				case "g": case "gb": case "gib": factor = 1024d * 1024d * 1024d; break;
				case "t": case "tb": case "tib": factor = 1024d * 1024d * 1024d * 1024d; break;
				default: return false;
			}
			bytes = (long)Math.Round(number * factor);
			return true;
		}

		/// <summary>"90" (minutes), "1h30m", "45m", "30s", "1.5h", "1:30:00" (h:m:s), "45:10" (m:s). Result in seconds.</summary>
		internal static bool TryParseDuration(string text, out double seconds) {
			seconds = 0;
			text = text.Trim();
			if (text.Length == 0) return false;

			if (text.Contains(':')) {
				string[] parts = text.Split(':');
				if (parts.Length < 2 || parts.Length > 3) return false;
				var v = new double[parts.Length];
				for (int i = 0; i < parts.Length; i++)
					if (!TryParseNumber(parts[i], out v[i])) return false;
				seconds = parts.Length == 3 ? v[0] * 3600 + v[1] * 60 + v[2] : v[0] * 60 + v[1];
				return true;
			}

			if (TryParseNumber(text, out double minutes)) { seconds = minutes * 60; return true; }

			var matches = DurationPartRegex.Matches(text);
			if (matches.Count == 0 || DurationPartRegex.Replace(text, string.Empty).Trim().Length != 0) return false;
			foreach (Match m in matches) {
				if (!TryParseNumber(m.Groups[1].Value, out double n)) return false;
				char unit = char.ToLowerInvariant(m.Groups[2].Value[0]);
				seconds += unit == 'h' ? n * 3600 : unit == 'm' ? n * 60 : n;
			}
			return true;
		}

		/// <summary>"1080", "1080p", "1920x1080", "4k", "2k", "8k": the shorter side in pixels.</summary>
		internal static bool TryParseResolution(string text, out double shortSide) {
			shortSide = 0;
			string t = text.Trim().ToLowerInvariant();
			switch (t) {
				case "2k": shortSide = 1440; return true;
				case "4k": shortSide = 2160; return true;
				case "8k": shortSide = 4320; return true;
			}
			var m = ResolutionRegex.Match(text);
			if (!m.Success) return false;
			if (m.Groups[3].Success) {
				shortSide = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
				return true;
			}
			double w = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
			double h = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
			shortSide = Math.Min(w, h);
			return true;
		}

		/// <summary>"5000" (kbps), "5000kbps", "5mbps", "8m". Result in kbps (1 Mbps = 1000 kbps).</summary>
		internal static bool TryParseBitrate(string text, out double kbps) {
			kbps = 0;
			var m = BitrateRegex.Match(text);
			if (!m.Success) return false;
			if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
				return false;
			bool mega = m.Groups[2].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase);
			kbps = mega ? n * 1000 : n;
			return true;
		}

		/// <summary>yyyy-MM-dd, yyyy/MM/dd, dd/MM/yyyy, dd-MM-yyyy ... optionally with HH:mm[:ss].</summary>
		internal static bool TryParseDate(string text, out DateTime date, out bool dateOnly) {
			text = text.Trim();
			if (DateTime.TryParseExact(text, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) {
				dateOnly = true;
				return true;
			}
			dateOnly = false;
			return DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
		}

		/// <summary>The shorter side of a "1920x1080" style frame size, 0 when unknown.</summary>
		internal static double ShortSideOf(string? frameSize) {
			if (string.IsNullOrEmpty(frameSize)) return 0;
			int x = frameSize.IndexOf('x');
			if (x <= 0) return 0;
			if (!int.TryParse(frameSize.AsSpan(0, x), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)) return 0;
			if (!int.TryParse(frameSize.AsSpan(x + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) return 0;
			return Math.Min(w, h);
		}

		/// <summary>The ids of all groups that satisfy the query.</summary>
		internal HashSet<Guid> MatchingGroups<T>(IEnumerable<T> items, Func<T, Guid> groupOf, Func<T, FileRecord> recordOf) where T : class =>
			MatchingGroupsAndRows(items, groupOf, recordOf).Groups;

		/// <summary>
		/// The groups that satisfy the query plus, inside those groups, the files that satisfy every
		/// file term - the rows the search is really about. A query without file terms (only
		/// exclusions, total or count) counts every file of a passing group as a hit.
		/// </summary>
		internal (HashSet<Guid> Groups, HashSet<T> Rows) MatchingGroupsAndRows<T>(IEnumerable<T> items, Func<T, Guid> groupOf, Func<T, FileRecord> recordOf) where T : class {
			var groups = new HashSet<Guid>();
			var rows = new HashSet<T>();
			foreach (var group in items.GroupBy(groupOf)) {
				long total = 0;
				int count = 0;
				bool excluded = false;
				var hits = new List<T>();

				foreach (T item in group) {
					FileRecord r = recordOf(item);
					string path = r.Path ?? string.Empty;
					total += r.Size;
					count++;

					SplitFileName(path, out string fileName, out string nameNoExt);

					if (fileTerms.Count == 0 || AllMatch(fileTerms, r, path, fileName, nameNoExt))
						hits.Add(item);
					if (!excluded && AnyMatch(excludeTerms, r, path, fileName, nameNoExt))
						excluded = true;
				}

				if (hits.Count == 0 || excluded) continue;

				bool groupOk = true;
				foreach (var t in groupTerms) {
					double value = t.Field == Field.Total ? total : count;
					if (!CompareNumber(value, t)) { groupOk = false; break; }
				}
				if (!groupOk) continue;

				groups.Add(group.Key);
				foreach (T hit in hits) rows.Add(hit);
			}
			return (groups, rows);
		}

		/// <summary>Works the same on every OS: both \ and / count as folder separators.</summary>
		internal static void SplitFileName(string path, out string fileName, out string nameNoExt) {
			int slash = path.LastIndexOfAny(new[] { '\\', '/' });
			fileName = slash >= 0 ? path.Substring(slash + 1) : path;
			int dot = fileName.LastIndexOf('.');
			nameNoExt = dot > 0 ? fileName.Substring(0, dot) : fileName;
		}

		static bool AllMatch(List<Term> terms, FileRecord r, string path, string fileName, string nameNoExt) {
			foreach (var t in terms)
				if (!Matches(t, r, path, fileName, nameNoExt)) return false;
			return true;
		}

		static bool AnyMatch(List<Term> terms, FileRecord r, string path, string fileName, string nameNoExt) {
			foreach (var t in terms)
				if (Matches(t, r, path, fileName, nameNoExt)) return true;
			return false;
		}

		/// <summary>Evaluates the positive form of a term against one file.</summary>
		static bool Matches(Term t, FileRecord r, string path, string fileName, string nameNoExt) {
			bool exact = t.Op is Op.Exact or Op.NotExact;
			switch (t.Field) {
				case Field.Path:
					return TermTextMatches(path, t, exact);
				case Field.Name:
					return TermTextMatches(fileName, t, exact) || (exact && TextMatches(nameNoExt, t.Text, true));
				case Field.Ext: {
					int dot = fileName.LastIndexOf('.');
					string ext = dot >= 0 ? fileName.Substring(dot + 1) : string.Empty;
					foreach (string e in t.Extensions)
						if (ext.Equals(e, StringComparison.OrdinalIgnoreCase)) return true;
					return false;
				}
				case Field.Size: return CompareNumber(r.Size, t);
				case Field.Duration: return CompareNumber(r.DurationSeconds, t);
				case Field.Res: return CompareNumber(r.ShortSide, t);
				case Field.Bitrate: return CompareNumber(r.BitrateKbps, t);
				case Field.Fps: return CompareNumber(r.Fps, t);
				case Field.Created: return CompareNumber(r.CreatedTicks, t);
				case Field.Modified: return CompareNumber(r.ModifiedTicks, t);
				default: return false;
			}
		}

		static bool CompareNumber(double value, Term t) {
			switch (t.Op) {
				case Op.Greater: return value > t.Number;
				case Op.GreaterOrEqual: return value >= t.Number;
				case Op.Less: return value < t.Number;
				case Op.LessOrEqual: return value <= t.Number;
				default: return Math.Abs(value - t.Number) < 1e-6;
			}
		}

		/// <summary>A multi-word contains-term needs every word, in any order; everything else is a single text match.</summary>
		static bool TermTextMatches(string value, Term t, bool exact) {
			if (!exact && t.Words != null) {
				foreach (string word in t.Words)
					if (!value.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
				return true;
			}
			return TextMatches(value, t.Text, exact);
		}

		/// <summary>
		/// Case-insensitive. Without wildcards: equality (exact) or substring. With * or ? the
		/// value is a wildcard pattern; unless exact it may match anywhere in the text.
		/// </summary>
		static bool TextMatches(string value, string pattern, bool exact) {
			if (pattern.IndexOfAny(new[] { '*', '?' }) >= 0) {
				string p = pattern.Replace("\\", "\\\\"); // backslashes are literal in paths, not escapes
				if (!exact) {
					if (!p.StartsWith('*')) p = "*" + p;
					if (!p.EndsWith('*')) p += "*";
				}
				return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p, value);
			}
			return exact
				? value.Equals(pattern, StringComparison.OrdinalIgnoreCase)
				: value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
		}
	}
}
