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

using System.Reactive;
using System.Text;
using Avalonia.Threading;
using ReactiveUI;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	/// <summary>
	/// The "Filters" panel of the results toolbar. Every box is optional: an empty box means that
	/// filter is off. The boxes are turned into the same "field operator value" terms the search
	/// box understands (see <see cref="ResultsSearchQuery"/>) and everything is combined, so all
	/// active filters have to match together. Values that cannot be understood are ignored.
	/// </summary>
	public partial class MainWindowVM : ReactiveObject {
		DispatcherTimer? _filterPanelTimer;
		int _activeFilterCount;

		/// <summary>How many filter rows of the panel currently have a usable value.</summary>
		public int ActiveFilterCount => _activeFilterCount;

		public string FiltersButtonText => _activeFilterCount > 0 ? "Filters (" + _activeFilterCount + ")" : "Filters";

		/// <summary>Results refresh this long after the last keystroke (or at once with Apply / Enter).</summary>
		const int FilterApplyDelayMs = 1500;

		/// <summary>What is typed in the search box. It only reaches <see cref="FilterByPath"/> (the applied search) on apply.</summary>
		string _filterSearchText = string.Empty;
		public string FilterSearchText {
			get => _filterSearchText;
			set {
				value ??= string.Empty;
				if (_filterSearchText == value) return;
				_filterSearchText = value;
				this.RaisePropertyChanged(nameof(FilterSearchText));
				ScheduleFilterApply();
			}
		}

		/// <summary>Applies the search box and the filter panel right now.</summary>
		public ReactiveCommand<Unit, Unit> ApplyFiltersCommand => ReactiveCommand.Create(() => {
			ApplyFiltersNow(true);
		});

		public ReactiveCommand<Unit, Unit> ResetFiltersCommand => ReactiveCommand.Create(() => {
			ResetFilterPanel();
			ApplyFiltersNow(true);
		});

		void ResetFilterPanel() {
			FilterNameText = string.Empty; FilterNameExact = false; FilterNameExclude = false;
			FilterPathText = string.Empty; FilterPathExact = false; FilterPathExclude = false;
			FilterExtensions = string.Empty;
			FilterSizeMin = string.Empty; FilterSizeMax = string.Empty;
			FilterTotalMin = string.Empty; FilterTotalMax = string.Empty;
			FilterDurationMin = string.Empty; FilterDurationMax = string.Empty;
			FilterResolutionMin = string.Empty; FilterResolutionMax = string.Empty;
			FilterBitrateMin = string.Empty; FilterBitrateMax = string.Empty;
			FilterFpsMin = string.Empty; FilterFpsMax = string.Empty;
			FilterCreatedFrom = string.Empty; FilterCreatedTo = string.Empty;
			FilterModifiedFrom = string.Empty; FilterModifiedTo = string.Empty;
			FilterCountMin = string.Empty; FilterCountMax = string.Empty;
		}

		// ---------------------------------------------------------------- results label
		string _filterResultsText = string.Empty;

		/// <summary>"Showing 24 of 1,230 files in 11 of 540 groups" while any filter is active, otherwise empty.</summary>
		public string FilterResultsText => _filterResultsText;
		public bool IsFilterResultsVisible => _filterResultsText.Length > 0;

		bool IsAnyResultsFilterActive() =>
			_searchQuery != null || !string.IsNullOrEmpty(FilterByPath)
			|| (FileType != null && FileType.Value != FileTypeFilter.All)
			|| FilterSimilarityFrom > 0 || FilterSimilarityTo < 100
			|| FilterGroupsWithCheckedItems || FilterHideGroupsWithOneFileLeft || FilterOnlyGroupsWithAiMatches;

		/// <summary>Called at the end of every results rebuild, so the label also follows deletes and imports.</summary>
		internal void UpdateFilterResultsText() {
			UpdateRowFilterToggleVisible();
			string text = string.Empty;
			if (Duplicates.Count > 0 && IsAnyResultsFilterActive()) {
				int shownFiles = 0;
				foreach (var group in resultsGroups) shownFiles += group.FileCount;
				var allGroups = new HashSet<Guid>();
				int hiddenChecked = 0;
				foreach (var d in Duplicates) {
					allGroups.Add(d.ItemInfo.GroupId);
					if (d.Checked && !d.IsVisibleInFilter) hiddenChecked++;
				}
				text = "Showing " + shownFiles.ToString("N0") + " of " + Duplicates.Count.ToString("N0") + " files in "
					+ resultsGroups.Count.ToString("N0") + " of " + allGroups.Count.ToString("N0") + " groups";
				// Checked files the filters hide are easy to forget: say so.
				if (hiddenChecked > 0)
					text += " · " + hiddenChecked.ToString("N0") + " checked " + (hiddenChecked == 1 ? "file is" : "files are") + " hidden";
			}
			if (text == _filterResultsText) return;
			_filterResultsText = text;
			this.RaisePropertyChanged(nameof(FilterResultsText));
			this.RaisePropertyChanged(nameof(IsFilterResultsVisible));
		}

		// ---------------------------------------------------------------- text filters
		string _filterNameText = string.Empty;
		public string FilterNameText { get => _filterNameText; set => SetPanelText(ref _filterNameText, value, nameof(FilterNameText)); }
		bool _filterNameExact;
		public bool FilterNameExact { get => _filterNameExact; set => SetPanelFlag(ref _filterNameExact, value, nameof(FilterNameExact)); }
		bool _filterNameExclude;
		public bool FilterNameExclude { get => _filterNameExclude; set => SetPanelFlag(ref _filterNameExclude, value, nameof(FilterNameExclude)); }

		string _filterPathText = string.Empty;
		public string FilterPathText { get => _filterPathText; set => SetPanelText(ref _filterPathText, value, nameof(FilterPathText)); }
		bool _filterPathExact;
		public bool FilterPathExact { get => _filterPathExact; set => SetPanelFlag(ref _filterPathExact, value, nameof(FilterPathExact)); }
		bool _filterPathExclude;
		public bool FilterPathExclude { get => _filterPathExclude; set => SetPanelFlag(ref _filterPathExclude, value, nameof(FilterPathExclude)); }

		string _filterExtensions = string.Empty;
		public string FilterExtensions { get => _filterExtensions; set => SetPanelText(ref _filterExtensions, value, nameof(FilterExtensions)); }

		// ---------------------------------------------------------------- range filters
		string _filterSizeMin = string.Empty;
		public string FilterSizeMin { get => _filterSizeMin; set => SetPanelText(ref _filterSizeMin, value, nameof(FilterSizeMin)); }
		string _filterSizeMax = string.Empty;
		public string FilterSizeMax { get => _filterSizeMax; set => SetPanelText(ref _filterSizeMax, value, nameof(FilterSizeMax)); }

		string _filterTotalMin = string.Empty;
		public string FilterTotalMin { get => _filterTotalMin; set => SetPanelText(ref _filterTotalMin, value, nameof(FilterTotalMin)); }
		string _filterTotalMax = string.Empty;
		public string FilterTotalMax { get => _filterTotalMax; set => SetPanelText(ref _filterTotalMax, value, nameof(FilterTotalMax)); }

		string _filterDurationMin = string.Empty;
		public string FilterDurationMin { get => _filterDurationMin; set => SetPanelText(ref _filterDurationMin, value, nameof(FilterDurationMin)); }
		string _filterDurationMax = string.Empty;
		public string FilterDurationMax { get => _filterDurationMax; set => SetPanelText(ref _filterDurationMax, value, nameof(FilterDurationMax)); }

		string _filterResolutionMin = string.Empty;
		public string FilterResolutionMin { get => _filterResolutionMin; set => SetPanelText(ref _filterResolutionMin, value, nameof(FilterResolutionMin)); }
		string _filterResolutionMax = string.Empty;
		public string FilterResolutionMax { get => _filterResolutionMax; set => SetPanelText(ref _filterResolutionMax, value, nameof(FilterResolutionMax)); }

		string _filterBitrateMin = string.Empty;
		public string FilterBitrateMin { get => _filterBitrateMin; set => SetPanelText(ref _filterBitrateMin, value, nameof(FilterBitrateMin)); }
		string _filterBitrateMax = string.Empty;
		public string FilterBitrateMax { get => _filterBitrateMax; set => SetPanelText(ref _filterBitrateMax, value, nameof(FilterBitrateMax)); }

		string _filterFpsMin = string.Empty;
		public string FilterFpsMin { get => _filterFpsMin; set => SetPanelText(ref _filterFpsMin, value, nameof(FilterFpsMin)); }
		string _filterFpsMax = string.Empty;
		public string FilterFpsMax { get => _filterFpsMax; set => SetPanelText(ref _filterFpsMax, value, nameof(FilterFpsMax)); }

		string _filterCreatedFrom = string.Empty;
		public string FilterCreatedFrom { get => _filterCreatedFrom; set => SetPanelText(ref _filterCreatedFrom, value, nameof(FilterCreatedFrom)); }
		string _filterCreatedTo = string.Empty;
		public string FilterCreatedTo { get => _filterCreatedTo; set => SetPanelText(ref _filterCreatedTo, value, nameof(FilterCreatedTo)); }

		string _filterModifiedFrom = string.Empty;
		public string FilterModifiedFrom { get => _filterModifiedFrom; set => SetPanelText(ref _filterModifiedFrom, value, nameof(FilterModifiedFrom)); }
		string _filterModifiedTo = string.Empty;
		public string FilterModifiedTo { get => _filterModifiedTo; set => SetPanelText(ref _filterModifiedTo, value, nameof(FilterModifiedTo)); }

		string _filterCountMin = string.Empty;
		public string FilterCountMin { get => _filterCountMin; set => SetPanelText(ref _filterCountMin, value, nameof(FilterCountMin)); }
		string _filterCountMax = string.Empty;
		public string FilterCountMax { get => _filterCountMax; set => SetPanelText(ref _filterCountMax, value, nameof(FilterCountMax)); }

		// ---------------------------------------------------------------- plumbing
		void SetPanelText(ref string field, string? value, string propertyName) {
			value ??= string.Empty;
			if (field == value) return;
			field = value;
			this.RaisePropertyChanged(propertyName);
			OnFilterPanelChanged();
		}

		void SetPanelFlag(ref bool field, bool value, string propertyName) {
			if (field == value) return;
			field = value;
			this.RaisePropertyChanged(propertyName);
			OnFilterPanelChanged();
		}

		/// <summary>Updates the active-filter counter at once; the results follow after a pause in typing.</summary>
		void OnFilterPanelChanged() {
			BuildPanelQueryText(out int rows);
			if (rows != _activeFilterCount) {
				_activeFilterCount = rows;
				this.RaisePropertyChanged(nameof(ActiveFilterCount));
				this.RaisePropertyChanged(nameof(FiltersButtonText));
			}
			ScheduleFilterApply();
		}

		/// <summary>(Re)starts the pause timer. Every further keystroke postpones the refresh.</summary>
		void ScheduleFilterApply() {
			if (_filterPanelTimer == null) {
				var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FilterApplyDelayMs) };
				timer.Tick += (_, __) => {
					timer.Stop();
					ApplyFiltersNow(false);
				};
				_filterPanelTimer = timer;
			}
			_filterPanelTimer.Stop();
			_filterPanelTimer.Start();
		}

		string _lastAppliedFilterKey = "\n";

		/// <summary>
		/// Makes the search box and the panel take effect. When it comes from the pause timer
		/// (<paramref name="force"/> false) a lone first character is not applied yet - a full
		/// scan for one letter is slow and rarely what is wanted - and nothing happens at all
		/// when the typing ended up exactly where the last apply was.
		/// </summary>
		void ApplyFiltersNow(bool force) {
			_filterPanelTimer?.Stop();

			string text = _filterSearchText;
			bool applySearch = force || text != FilterByPath;
			if (!force && applySearch && text.Trim().Length == 1) applySearch = false;

			string searchToUse = applySearch ? text : FilterByPath;
			string key = searchToUse + "\n" + BuildPanelQueryText(out _);
			if (!force && key == _lastAppliedFilterKey) return;
			_lastAppliedFilterKey = key;

			if (applySearch) FilterByPath = text;
			RebuildSearchPathIndex();
			RefreshResultsView();
		}

		/// <summary>
		/// The panel as search terms, e.g. <c>name:trip size&gt;=500MB res&gt;=1080</c>. Empty boxes and
		/// values that cannot be parsed produce nothing. <paramref name="activeRows"/> is the number
		/// of panel rows that contributed at least one term.
		/// </summary>
		string BuildPanelQueryText(out int activeRows) {
			var sb = new StringBuilder();
			int rows = 0;

			bool Emit(string field, string op, string? value) {
				value = (value ?? string.Empty).Replace("\"", string.Empty).Trim();
				if (value.Length == 0) return false;
				bool hasSpace = false;
				foreach (char c in value)
					if (char.IsWhiteSpace(c)) { hasSpace = true; break; }
				string token = field + op + (hasSpace ? "\"" + value + "\"" : value);
				if (!ResultsSearchQuery.IsValidTerm(token)) return false;
				if (sb.Length > 0) sb.Append(' ');
				sb.Append(token);
				return true;
			}

			int Range(string field, string? min, string? max) {
				bool a = Emit(field, ">=", min);
				bool b = Emit(field, "<=", max);
				return a || b ? 1 : 0;
			}

			string TextOp(bool exact, bool exclude) => exclude ? (exact ? "!=" : "!:") : (exact ? "=" : ":");

			if (Emit("name", TextOp(FilterNameExact, FilterNameExclude), FilterNameText)) rows++;
			if (Emit("path", TextOp(FilterPathExact, FilterPathExclude), FilterPathText)) rows++;
			if (Emit("ext", ":", FilterExtensions)) rows++;
			rows += Range("size", FilterSizeMin, FilterSizeMax);
			rows += Range("total", FilterTotalMin, FilterTotalMax);
			rows += Range("duration", FilterDurationMin, FilterDurationMax);
			rows += Range("res", FilterResolutionMin, FilterResolutionMax);
			rows += Range("bitrate", FilterBitrateMin, FilterBitrateMax);
			rows += Range("fps", FilterFpsMin, FilterFpsMax);
			rows += Range("created", FilterCreatedFrom, FilterCreatedTo);
			rows += Range("modified", FilterModifiedFrom, FilterModifiedTo);
			rows += Range("count", FilterCountMin, FilterCountMax);

			activeRows = rows;
			return sb.ToString();
		}

		static ResultsSearchQuery.FileRecord ToFileRecord(DuplicateItemVM d) {
			var i = d.ItemInfo;
			return new ResultsSearchQuery.FileRecord(
				i.Path,
				i.SizeLong,
				i.Duration.TotalSeconds,
				ResultsSearchQuery.ShortSideOf(i.FrameSize),
				(double)i.BitRateKbs,
				i.Fps,
				i.DateCreated.Ticks,
				i.DateModified.Ticks);
		}
	}
}
