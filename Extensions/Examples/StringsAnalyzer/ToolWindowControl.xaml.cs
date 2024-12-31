using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using dnlib.DotNet;
using System.ComponentModel.Composition;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Decompiler;
using static StringsAnalyzer.Extension.ToolWindowVM;
using System.Linq;

namespace StringsAnalyzer.Extension {
	public partial class ToolWindowControl : UserControl {
		private List<StringAnalyzerData> AllItems = new List<StringAnalyzerData>();
		private List<StringAnalyzerData> FilteredItems = new List<StringAnalyzerData>();
		public static ListView stringAnalyzer = new ListView();
		internal IInputElement option1TextBox;

		private readonly Lazy<IDocumentTabService> documentTabService;
		private readonly IDecompilerService decompilerService;
		private System.Text.RegularExpressions.Regex? currentRegex;
		private string currentSearchText = "";
		private bool isRegexSearch;
		private bool isCaseSensitive;

		[ImportingConstructor]
		public ToolWindowControl(Lazy<IDocumentTabService> documentTabService, IDecompilerService decompilerService) {
			this.documentTabService = documentTabService;
			this.decompilerService = decompilerService;
			InitializeComponent();
			
			// Add double click handler
			lvStringsAnalyzer.MouseDoubleClick += LvStringsAnalyzer_MouseDoubleClick;

			// Subscribe to document changes
			documentTabService.Value.DocumentTreeView.DocumentService.CollectionChanged += DocumentService_CollectionChanged;
		}

		private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) {
			currentSearchText = searchTextBox.Text;
			UpdateSearch();
		}

		private void RegexCheckBox_CheckedChanged(object sender, RoutedEventArgs e) {
			isRegexSearch = regexCheckBox.IsChecked == true;
			UpdateSearch();
		}

		private void CaseSensitiveCheckBox_CheckedChanged(object sender, RoutedEventArgs e) {
			isCaseSensitive = caseSensitiveCheckBox.IsChecked == true;
			UpdateSearch();
		}

		private void UpdateSearch() {
			try {
				if (string.IsNullOrWhiteSpace(currentSearchText)) {
					FilteredItems = new List<StringAnalyzerData>(AllItems);
					searchStatusText.Text = $"Showing all {AllItems.Count} items";
				}
				else {
					if (isRegexSearch) {
						try {
							var options = isCaseSensitive ? 
								System.Text.RegularExpressions.RegexOptions.None : 
								System.Text.RegularExpressions.RegexOptions.IgnoreCase;
							
							currentRegex = new System.Text.RegularExpressions.Regex(currentSearchText, options);
							FilteredItems = AllItems.Where(item => 
								currentRegex.IsMatch(item.StringValue ?? "") ||
								currentRegex.IsMatch(item.MdName ?? "") ||
								currentRegex.IsMatch(item.FullmdName ?? "")
							).ToList();
							
							searchStatusText.Text = $"Found {FilteredItems.Count} matches (Regex)";
						}
						catch (ArgumentException) {
							searchStatusText.Text = "Invalid regular expression";
							FilteredItems = new List<StringAnalyzerData>();
						}
					}
					else {
						var comparison = isCaseSensitive ? 
							StringComparison.Ordinal : 
							StringComparison.OrdinalIgnoreCase;
						
						FilteredItems = AllItems.Where(item =>
							(item.StringValue?.IndexOf(currentSearchText, comparison) >= 0) ||
							(item.MdName?.IndexOf(currentSearchText, comparison) >= 0) ||
							(item.FullmdName?.IndexOf(currentSearchText, comparison) >= 0)
						).ToList();
						
						searchStatusText.Text = $"Found {FilteredItems.Count} matches";
					}
				}

				stringAnalyzer = lvStringsAnalyzer;
				stringAnalyzer.ItemsSource = FilteredItems;
			}
			catch (Exception ex) {
				searchStatusText.Text = $"Search error: {ex.Message}";
			}
		}

		private void AnalyzeButton_Click(object sender, RoutedEventArgs e) {
			AllItems.Clear();
			FilteredItems.Clear();

			var document = documentTabService.Value.DocumentTreeView.DocumentService.GetDocuments().FirstOrDefault();
			var module = document?.ModuleDef as ModuleDefMD;

			if (module == null) {
				MessageBox.Show("No assembly is currently loaded");
				return;
			}

			try {
				var analyzer = new StringAnalyzer(module);
				var results = analyzer.Analyze();

				if (results.Count == 0) {
					MessageBox.Show("No strings found in the current assembly");
					return;
				}

				AllItems.AddRange(results);
				UpdateSearch(); // Apply any existing search filter
			}
			catch (Exception ex) {
				MessageBox.Show($"Error analyzing strings: {ex.Message}");
			}
		}

		private void ClearButton_Click(object sender, RoutedEventArgs e) {
			AllItems.Clear();
			FilteredItems.Clear();
			stringAnalyzer.ItemsSource = null;
			searchTextBox.Clear();
			searchStatusText.Text = "Type to search...";
		}

		private void LvStringsAnalyzer_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
			var item = lvStringsAnalyzer.SelectedItem as StringAnalyzerData;
			if (item != null) {
				NavigateToReference(item, false);
			}
		}

		private void MenuItem_GoToReference(object sender, RoutedEventArgs e) {
			var item = lvStringsAnalyzer.SelectedItem as StringAnalyzerData;
			if (item != null) {
				NavigateToReference(item, false);
			}
		}

		private void MenuItem_GoToReferenceNewTab(object sender, RoutedEventArgs e) {
			var item = lvStringsAnalyzer.SelectedItem as StringAnalyzerData;
			if (item != null) {
				NavigateToReference(item, true);
			}
		}

		private void MenuItem_CopyValue(object sender, RoutedEventArgs e) {
			var item = lvStringsAnalyzer.SelectedItem as StringAnalyzerData;
			if (item?.StringValue != null) {
				try {
					Clipboard.SetText(item.StringValue);
				}
				catch (Exception) { }
			}
		}

		private void MenuItem_CopyFullInfo(object sender, RoutedEventArgs e) {
			var item = lvStringsAnalyzer.SelectedItem as StringAnalyzerData;
			if (item != null) {
				try {
					var info = $"String: {item.StringValue}\n" +
						$"IL Offset: {item.IlOffset}\n" +
						$"Method: {item.FullmdName}\n" +
						$"Token: {item.MdToken}\n" +
						$"Module: {item.ModuleID}";
					Clipboard.SetText(info);
				}
				catch (Exception) { }
			}
		}

		private void NavigateToReference(StringAnalyzerData item, bool newTab) {
			if (item.Method != null && item.Offset.HasValue) {
				documentTabService.Value.FollowReference(item.Method, newTab);
			}
		}

		private void DocumentService_CollectionChanged(object? sender, EventArgs e) {
			// Clear results when documents change
			AllItems.Clear();
			FilteredItems.Clear();
			stringAnalyzer.ItemsSource = null;
			searchTextBox?.Clear();
			if (searchStatusText != null)
				searchStatusText.Text = "Type to search...";

			// Update button state
			if (analyzeButton != null)
				analyzeButton.IsEnabled = documentTabService.Value.DocumentTreeView.DocumentService.GetDocuments().Any();
		}

		public void OnClose() {
			// Unsubscribe from events
			if (documentTabService.Value != null) {
				documentTabService.Value.DocumentTreeView.DocumentService.CollectionChanged -= DocumentService_CollectionChanged;
			}
		}
	}
}
