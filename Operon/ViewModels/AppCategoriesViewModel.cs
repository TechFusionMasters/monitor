using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SystemActivityTracker.Models;
using SystemActivityTracker.Services;
using SystemActivityTracker.Services.Abstractions;
using SystemActivityTracker.Utilities;

namespace SystemActivityTracker.ViewModels
{
    // One category row on the Settings tab. Owns its own process-name chip list so the
    // Add/Remove-process commands don't need any knowledge of sibling categories.
    public sealed class AppCategoryItem : INotifyPropertyChanged
    {
        private string _name;
        private string _pendingProcessName = string.Empty;

        public AppCategoryItem(Guid id, string name, bool isProtected, IEnumerable<string> processNames)
        {
            Id = id;
            _name = name;
            IsProtected = isProtected;
            ProcessNames = new ObservableCollection<string>(processNames);

            AddProcessCommand = new RelayCommand(_ => AddProcess(), _ => !string.IsNullOrWhiteSpace(PendingProcessName));
            RemoveProcessCommand = new RelayCommand(p => RemoveProcess(p as string));
        }

        public Guid Id { get; }
        public bool IsProtected { get; }

        public string Name
        {
            get => _name;
            set
            {
                if (!string.Equals(_name, value, StringComparison.Ordinal))
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PendingProcessName
        {
            get => _pendingProcessName;
            set
            {
                if (!string.Equals(_pendingProcessName, value, StringComparison.Ordinal))
                {
                    _pendingProcessName = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<string> ProcessNames { get; }

        public ICommand AddProcessCommand { get; }
        public ICommand RemoveProcessCommand { get; }

        private void AddProcess()
        {
            var name = PendingProcessName?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!ProcessNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
            {
                ProcessNames.Add(name);
            }

            PendingProcessName = string.Empty;
        }

        private void RemoveProcess(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var match = ProcessNames.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                ProcessNames.Remove(match);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // A process name seen in recent activity logs that isn't mapped to any category yet.
    // SelectedCategory is set by the ComboBox in the Settings UI; picking one assigns it.
    public sealed class UnknownAppItem : INotifyPropertyChanged
    {
        private AppCategoryItem? _selectedCategory;

        public string ProcessName { get; init; } = string.Empty;

        public AppCategoryItem? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (!ReferenceEquals(_selectedCategory, value))
                {
                    _selectedCategory = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Application Categories section on the Settings tab. Mutations apply to the
    // in-memory list immediately (so the UI reflects them right away) but only persist
    // to disk — and only propagate to the Application Usage breakdown — once the user
    // clicks Save, matching the rest of the Settings tab's "edit, then Save" convention.
    public sealed class AppCategoriesViewModel : INotifyPropertyChanged
    {
        private readonly AppCategoryService _categoryService;
        private readonly IActivityLogReader _activityLogReader;
        private string _newCategoryName = string.Empty;
        private string _saveStatusText = string.Empty;

        public event EventHandler? CategoriesSaved;

        public AppCategoriesViewModel(
            AppCategoryService? categoryService = null,
            IActivityLogReader? activityLogReader = null)
        {
            _categoryService = categoryService ?? new AppCategoryService();
            _activityLogReader = activityLogReader ?? new ActivityLogReader();

            AddCategoryCommand = new RelayCommand(_ => AddCategory(), _ => !string.IsNullOrWhiteSpace(NewCategoryName));
            DeleteCategoryCommand = new RelayCommand(p => DeleteCategory(p as AppCategoryItem), p => p is AppCategoryItem item && !item.IsProtected);
            AssignUnknownAppCommand = new RelayCommand(p => AssignUnknownApp(p as UnknownAppItem), p => (p as UnknownAppItem)?.SelectedCategory != null);
            ScanForNewAppsCommand = new RelayCommand(_ => ScanForUnknownApps());
            SaveCommand = new RelayCommand(_ => Save());

            LoadFromDisk();
        }

        public ObservableCollection<AppCategoryItem> Categories { get; } = new ObservableCollection<AppCategoryItem>();
        public ObservableCollection<UnknownAppItem> UnknownApps { get; } = new ObservableCollection<UnknownAppItem>();

        public string NewCategoryName
        {
            get => _newCategoryName;
            set
            {
                if (!string.Equals(_newCategoryName, value, StringComparison.Ordinal))
                {
                    _newCategoryName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SaveStatusText
        {
            get => _saveStatusText;
            private set
            {
                if (!string.Equals(_saveStatusText, value, StringComparison.Ordinal))
                {
                    _saveStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasUnknownApps => UnknownApps.Count > 0;

        public ICommand AddCategoryCommand { get; }
        public ICommand DeleteCategoryCommand { get; }
        public ICommand AssignUnknownAppCommand { get; }
        public ICommand ScanForNewAppsCommand { get; }
        public ICommand SaveCommand { get; }

        private void LoadFromDisk()
        {
            Categories.Clear();
            foreach (var category in _categoryService.LoadAll())
            {
                Categories.Add(new AppCategoryItem(category.Id, category.Name, category.IsProtected, category.ProcessNames));
            }

            ScanForUnknownApps();
        }

        private void AddCategory()
        {
            var name = NewCategoryName?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                SaveStatusText = $"\"{name}\" already exists.";
                return;
            }

            var others = Categories.FirstOrDefault(c => c.IsProtected);
            var insertIndex = others != null ? Categories.IndexOf(others) : Categories.Count;
            Categories.Insert(insertIndex, new AppCategoryItem(Guid.NewGuid(), name, false, Array.Empty<string>()));

            NewCategoryName = string.Empty;
            SaveStatusText = string.Empty;
        }

        private void DeleteCategory(AppCategoryItem? item)
        {
            if (item == null || item.IsProtected)
            {
                return;
            }

            var others = Categories.FirstOrDefault(c => c.IsProtected);
            if (others != null)
            {
                foreach (var processName in item.ProcessNames)
                {
                    if (!others.ProcessNames.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase)))
                    {
                        others.ProcessNames.Add(processName);
                    }
                }
            }

            Categories.Remove(item);
        }

        private void ScanForUnknownApps()
        {
            var categories = Categories.Select(ToModel).ToList();
            var unmapped = _categoryService.DetectUnmappedProcessNames(_activityLogReader, categories, DateTime.Today);

            UnknownApps.Clear();
            foreach (var processName in unmapped)
            {
                UnknownApps.Add(new UnknownAppItem { ProcessName = processName });
            }

            OnPropertyChanged(nameof(HasUnknownApps));
        }

        private void AssignUnknownApp(UnknownAppItem? item)
        {
            if (item?.SelectedCategory == null || string.IsNullOrWhiteSpace(item.ProcessName))
            {
                return;
            }

            if (!item.SelectedCategory.ProcessNames.Any(p => string.Equals(p, item.ProcessName, StringComparison.OrdinalIgnoreCase)))
            {
                item.SelectedCategory.ProcessNames.Add(item.ProcessName);
            }

            UnknownApps.Remove(item);
            OnPropertyChanged(nameof(HasUnknownApps));
        }

        private void Save()
        {
            var categories = Categories.Select(ToModel).ToList();
            _categoryService.SaveAll(categories);

            SaveStatusText = "Saved.";
            CategoriesSaved?.Invoke(this, EventArgs.Empty);
        }

        private static AppCategory ToModel(AppCategoryItem item) => new AppCategory
        {
            Id = item.Id,
            Name = item.Name,
            IsProtected = item.IsProtected,
            ProcessNames = item.ProcessNames.ToList()
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
