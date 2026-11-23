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
using ReactiveUI;

namespace VDF.GUI.ViewModels {
	/// <summary>
	/// Order of the member rows inside every group, picked from the column headers. It is
	/// independent of the group sort in the toolbar; both apply together. None keeps the order that
	/// follows the group sort (the behaviour before this option existed).
	/// </summary>
	public enum ResultsRowSort {
		None,
		NameAsc, NameDesc,
		DurationDesc, DurationAsc,
		ResolutionDesc, ResolutionAsc,
		SizeDesc, SizeAsc,
		DateNewest, DateOldest,
		BitrateDesc, BitrateAsc,
		SimilarityDesc, SimilarityAsc,
	}

	public partial class MainWindowVM : ReactiveObject {
		// ---------------------------------------------------------------- row sort (column headers)
		ResultsRowSort _resultsRowSortChoice = ResultsRowSort.None;

		public ResultsRowSort ResultsRowSortChoice {
			get => _resultsRowSortChoice;
			set {
				if (value == _resultsRowSortChoice) return;
				_resultsRowSortChoice = value;
				this.RaisePropertyChanged(nameof(ResultsRowSortChoice));
				this.RaisePropertyChanged(nameof(IsResultsRowSortActive));
				this.RaisePropertyChanged(nameof(ResultsRowSortText));
				this.RaisePropertyChanged(nameof(RowSortArrowFile));
				this.RaisePropertyChanged(nameof(RowSortArrowDuration));
				this.RaisePropertyChanged(nameof(RowSortArrowSize));
				this.RaisePropertyChanged(nameof(RowSortArrowBitrate));
				this.RaisePropertyChanged(nameof(RowSortArrowMatch));
				RefreshResultsView();
			}
		}

		public bool IsResultsRowSortActive => _resultsRowSortChoice != ResultsRowSort.None;

		/// <summary>"Rows: Duration, longest first" - shown in the toolbar next to a clear button.</summary>
		public string ResultsRowSortText => _resultsRowSortChoice == ResultsRowSort.None
			? string.Empty
			: "Rows: " + DescribeRowSort(_resultsRowSortChoice);

		// Header markers: ▼ = largest / newest / Z first, ▲ = smallest / oldest / A first.
		public string RowSortArrowFile => ArrowFor(ResultsRowSort.NameDesc, ResultsRowSort.NameAsc);
		public string RowSortArrowDuration => ArrowFor(ResultsRowSort.DurationDesc, ResultsRowSort.DurationAsc)
			+ ArrowFor(ResultsRowSort.ResolutionDesc, ResultsRowSort.ResolutionAsc);
		public string RowSortArrowSize => ArrowFor(ResultsRowSort.SizeDesc, ResultsRowSort.SizeAsc)
			+ ArrowFor(ResultsRowSort.DateNewest, ResultsRowSort.DateOldest);
		public string RowSortArrowBitrate => ArrowFor(ResultsRowSort.BitrateDesc, ResultsRowSort.BitrateAsc);
		public string RowSortArrowMatch => ArrowFor(ResultsRowSort.SimilarityDesc, ResultsRowSort.SimilarityAsc);

		string ArrowFor(ResultsRowSort descending, ResultsRowSort ascending) =>
			_resultsRowSortChoice == descending ? "▼" : _resultsRowSortChoice == ascending ? "▲" : string.Empty;

		public ReactiveCommand<ResultsRowSort, Unit> SetResultsRowSortCommand => ReactiveCommand.Create<ResultsRowSort>(sort => {
			ResultsRowSortChoice = sort;
		});

		public ReactiveCommand<Unit, Unit> ClearResultsRowSortCommand => ReactiveCommand.Create(() => {
			ResultsRowSortChoice = ResultsRowSort.None;
		});

		internal static string DescribeRowSort(ResultsRowSort sort) {
			switch (sort) {
				case ResultsRowSort.NameAsc: return "File name A-Z";
				case ResultsRowSort.NameDesc: return "File name Z-A";
				case ResultsRowSort.DurationDesc: return "Duration, longest first";
				case ResultsRowSort.DurationAsc: return "Duration, shortest first";
				case ResultsRowSort.ResolutionDesc: return "Resolution, highest first";
				case ResultsRowSort.ResolutionAsc: return "Resolution, lowest first";
				case ResultsRowSort.SizeDesc: return "Size, largest first";
				case ResultsRowSort.SizeAsc: return "Size, smallest first";
				case ResultsRowSort.DateNewest: return "Date, newest first";
				case ResultsRowSort.DateOldest: return "Date, oldest first";
				case ResultsRowSort.BitrateDesc: return "Bitrate, highest first";
				case ResultsRowSort.BitrateAsc: return "Bitrate, lowest first";
				case ResultsRowSort.SimilarityDesc: return "Match, highest first";
				case ResultsRowSort.SimilarityAsc: return "Match, lowest first";
				default: return string.Empty;
			}
		}

		// ---------------------------------------------------------------- rows hidden by the search
		bool _showAllRowsOfMatchingGroups;

		/// <summary>
		/// Off (default): a group found by the search shows only the rows that match it. On: every
		/// row of the matching groups is shown. One switch for the whole result list.
		/// </summary>
		public bool ShowAllRowsOfMatchingGroups {
			get => _showAllRowsOfMatchingGroups;
			set {
				if (value == _showAllRowsOfMatchingGroups) return;
				_showAllRowsOfMatchingGroups = value;
				this.RaisePropertyChanged(nameof(ShowAllRowsOfMatchingGroups));
				RefreshResultsView();
			}
		}

		bool _rowFilterToggleVisible;

		/// <summary>The checkbox only makes sense while a search or panel filter is applied.</summary>
		public bool IsRowFilterToggleVisible => _rowFilterToggleVisible;

		void UpdateRowFilterToggleVisible() {
			bool visible = _searchQuery != null || !string.IsNullOrEmpty(FilterByPath);
			if (visible == _rowFilterToggleVisible) return;
			_rowFilterToggleVisible = visible;
			this.RaisePropertyChanged(nameof(IsRowFilterToggleVisible));
		}
	}
}
