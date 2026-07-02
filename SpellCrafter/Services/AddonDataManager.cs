using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SQLiteNetExtensions.Extensions;

namespace SpellCrafter.Services;

public static class AddonDataManager
{
    public static RangedObservableCollection<Addon> InstalledAddons { get; set; } = [];
    public static RangedObservableCollection<Addon> OnlineAddons { get; set; } = [];

    static AddonDataManager() // TODO move to startup waiting window
    {
        EsoDataConnection.CreateTablesIfNotExists();

        using var db = new EsoDataConnection();

        var recoveryService = new AddonOperationRecoveryService(AddonServices.JournalStore);
        recoveryService.RecoverIncompleteOperations(db);

        InstalledAddons.Refresh(GetAllLocalAddonsWithDetails(db));
        OnlineAddons.Refresh(GetAllOnlineAddonsWithDetails(db));
        SyncAddonLists(db);

        CheckAddonInstallationErrors(db);
    }

    private static void CheckAddonVersions(EsoDataConnection db) // TODO move to startup waiting window
    {
        var toUpdate = new List<Addon>();
        foreach (var installedAddon in InstalledAddons)
            if (installedAddon.State is AddonState.LatestVersion &&
                AddonVersionComparer.CompareVersions(installedAddon.Version, installedAddon.LatestVersion) < 0)
            {
                installedAddon.State = AddonState.Outdated;
                toUpdate.Add(installedAddon);
            }

        InsertOrUpdateLocalAddons(db, toUpdate);
    }

    private static void CheckAddonInstallationErrors(EsoDataConnection db)
    {
        var incorrectAddons =
            InstalledAddons.Where(installedAddon => installedAddon.State == AddonState.Installing).ToList();
        foreach (var addon in incorrectAddons)
            addon.State = AddonState.InstallationError;

        InsertOrUpdateLocalAddons(db, incorrectAddons);
    }

    public static void UpdateInstalledAddonsInfo(EsoDataConnection db, List<Addon> addons)
    {
        var filteredAddons = FilterAddonsWithLatestVersion(addons);
        InsertOrUpdateCommonAddons(db, filteredAddons);
        RefreshLocalAddons(db, filteredAddons);
        RefreshLocalAddonDependencies(db, filteredAddons);
        RemoveUnusedCommonAddons(db);

        InsertOrUpdateAuthors(db, filteredAddons);
        RemoveUnusedAuthors(db);
        InsertOrUpdateCategories(db, filteredAddons);
        RemoveUnusedCategories(db);

        InstalledAddons.Refresh(GetAllLocalAddonsWithDetails(db));
        SyncAddonLists(db);
    }

    public static void UpdateOnlineAddonsInfo(EsoDataConnection db, List<Addon> addons)
    {
        var filteredAddons = FilterAddonsWithLatestVersion(addons);
        InsertOrUpdateCommonAddons(db, filteredAddons);
        RefreshOnlineAddons(db, filteredAddons);
        RefreshOnlineAddonDependencies(db, filteredAddons);
        RemoveUnusedCommonAddons(db);

        InsertOrUpdateAuthors(db, filteredAddons);
        RemoveUnusedAuthors(db);
        InsertOrUpdateCategories(db, filteredAddons);
        RemoveUnusedCategories(db);

        OnlineAddons.Refresh(GetAllOnlineAddonsWithDetails(db));
        SyncAddonLists(db);
    }

    private static List<Addon> FilterAddonsWithLatestVersion(List<Addon> addons)
    {
        return addons.GroupBy(addon => (addon.Name, GetAuthorsKey(addon.Authors)))
            .Select(g =>
            {
                return g.Aggregate((currentMax, x) =>
                    AddonVersionComparer.CompareVersions(currentMax, x) > 0 ? currentMax : x);
            }).ToList();
    }

    public static void SyncAddonLists(EsoDataConnection db)
    {
        var installedAddonMap = InstalledAddons.ToDictionary(addon => addon.CommonAddonId, addon => addon);

        for (var i = 0; i < OnlineAddons.Count; ++i)
        {
            if (!installedAddonMap.TryGetValue(OnlineAddons[i].CommonAddonId, out var matchedAddon)) continue;

            matchedAddon.OnlineAddonId = OnlineAddons[i].OnlineAddonId;
            matchedAddon.OnlineDependencies = OnlineAddons[i].OnlineDependencies;
            matchedAddon.UniqueId = OnlineAddons[i].UniqueId;
            matchedAddon.LatestVersion = OnlineAddons[i].LatestVersion;
            matchedAddon.DisplayedLatestVersion = OnlineAddons[i].DisplayedLatestVersion;
            matchedAddon.Categories = OnlineAddons[i].Categories;
            OnlineAddons[i] = matchedAddon;
        }

        CheckAddonVersions(db);
    }

    public static RangedObservableCollection<Addon> GetAllLocalAddonsWithDetails(EsoDataConnection db)
    {
        var localAddons = db.GetAllWithChildren<LocalAddon>(recursive: true);

        var addonAuthors = db.Table<AddonAuthor>().ToList();
        var authors = db.Table<Author>().ToList().ToDictionary(a => a.Id);

        return new RangedObservableCollection<Addon>(localAddons.Select(localAddon =>
        {
            var commonAddon = localAddon.CommonAddon;

            var addonAuthorsList = addonAuthors.Where(aa => aa.CommonAddonId == commonAddon.Id)
                .Select(aa => authors[aa.AuthorId])
                .ToList();

            var addon = new Addon(AddonServices.InstallationService, AddonServices.OperationSubmissionService)
            {
                CommonAddonId = localAddon.CommonAddonId,
                LocalAddonId = localAddon.Id,
                Name = commonAddon.Name,
                Title = commonAddon.Title,
                Description = commonAddon.Description,
                State = localAddon.State,
                InstallationMethod = localAddon.InstallationMethod,
                Authors = new ObservableCollection<Author>(addonAuthorsList),
                Version = localAddon.Version,
                DisplayedVersion = localAddon.DisplayedVersion,
                LocalDependencies = new RangedObservableCollection<CommonAddon>(localAddon.Dependencies)
            };

            return addon;
        }).ToList());
    }

    public static RangedObservableCollection<Addon> GetAllOnlineAddonsWithDetails(EsoDataConnection db) // TODO change to pagination
    {
        var onlineAddons = db.GetAllWithChildren<OnlineAddon>(recursive: true);

        var addonAuthors = db.Table<AddonAuthor>().ToList();
        var authors = db.Table<Author>().ToList().ToDictionary(a => a.Id);
        var addonCategories = db.Table<AddonCategory>().ToList();
        var categories = db.Table<Category>().ToList().ToDictionary(c => c.Id);

        return new RangedObservableCollection<Addon>(onlineAddons.Select(onlineAddon =>
        {
            var commonAddon = onlineAddon.CommonAddon;

            var addonAuthorsList = addonAuthors.Where(aa => aa.CommonAddonId == commonAddon.Id)
                .Select(aa => authors[aa.AuthorId])
                .ToList();
            var addonCategoriesList = addonCategories.Where(ac => ac.CommonAddonId == commonAddon.Id)
                .Select(ac => categories[ac.CategoryId])
                .ToList();

            var addon = new Addon(AddonServices.InstallationService, AddonServices.OperationSubmissionService)
            {
                CommonAddonId = onlineAddon.CommonAddonId,
                OnlineAddonId = onlineAddon.Id,
                Name = commonAddon.Name,
                Title = commonAddon.Title,
                Description = commonAddon.Description,
                State = AddonState.NotInstalled,
                Categories = new ObservableCollection<Category>(addonCategoriesList),
                Authors = new ObservableCollection<Author>(addonAuthorsList),
                UniqueId = onlineAddon.UniqueId,
                LatestVersion = onlineAddon.LatestVersion,
                DisplayedLatestVersion = onlineAddon.DisplayedLatestVersion,
                OnlineDependencies = new RangedObservableCollection<CommonAddon>(onlineAddon.Dependencies)
            };

            return addon;
        }).ToList());
    }

    public static string GetAuthorsKey(IEnumerable<Author> authors)
    {
        // TODO replace with ids
        return string.Join(',', authors.Select(author => author.Name).OrderBy(name => name));
    }

    private static bool UpdateCommonAddonFieldsIfChanged(CommonAddon commonAddon, ICommonAddon updatedAddon)
    {
        var isChanged = false;

        if (commonAddon.Description != updatedAddon.Description)
        {
            commonAddon.Description = updatedAddon.Description;
            isChanged = true;
        }

        if (commonAddon.Title != updatedAddon.Title)
        {
            commonAddon.Title = updatedAddon.Title;
            isChanged = true;
        }

        return isChanged;
    }

    private static void InsertOrUpdateCommonAddons(EsoDataConnection db, List<Addon> updatedAddons)
    {
        if (updatedAddons.Count == 0)
            return;

        var commonAddons = db.GetAllWithChildren<CommonAddon>(recursive: true);
        var commonAddonsDictionary = commonAddons.ToDictionary(
            addon => (addon.Name, GetAuthorsKey(addon.Authors)), addon => addon);

        var toInsert = new List<CommonAddon>();
        var toUpdate = new List<CommonAddon>();

        foreach (var addon in updatedAddons)
        {
            var authorsKey = GetAuthorsKey(addon.Authors);
            var addonKey = (addon.Name, authorsKey);

            if (!commonAddonsDictionary.TryGetValue(addonKey, out var dbAddon))
            {
                dbAddon = new CommonAddon
                {
                    Name = addon.Name,
                    Title = addon.Title,
                    Description = addon.Description,
                    Authors = [.. addon.Authors]
                };
                toInsert.Add(dbAddon);
            }
            else
            {
                addon.CommonAddonId = dbAddon.Id;
                if (UpdateCommonAddonFieldsIfChanged(dbAddon, addon))
                    toUpdate.Add(dbAddon);
            }
        }

        var dependencies = updatedAddons
            .SelectMany(addon => addon.LocalDependencies.Concat(addon.OnlineDependencies))
            .ToList();

        foreach (var dependency in dependencies)
        {
            var commonAddon = commonAddons.Where(commonAddon => commonAddon.Name == dependency.Name)
                .Select(commonAddon => new
                    { Addon = commonAddon, Version = commonAddon.OnlineAddon?.LatestVersion })
                .OrderByDescending(a => a.Version, new AddonVersionComparer())
                .Select(a => a.Addon)
                .FirstOrDefault(); // TODO ask user which addon to choose
            if (commonAddon == null)
            {
                var index = toInsert
                                .Select((a, idx) => new { Addon = a, Index = idx })
                                .FirstOrDefault(a => a.Addon.Name == dependency.Name)?.Index ??
                            -1; // TODO think about which addon to associate the dependency to if multiple have same name
                if (index == -1)
                    toInsert.Add(dependency);
            }
            else
            {
                dependency.Id = commonAddon.Id;
            }
        }

        if (toInsert.Count != 0)
        {
            db.InsertAll(toInsert);

            foreach (var addon in toInsert)
            {
                var authorsKey = (addon.Name, GetAuthorsKey(addon.Authors));
                var updateAddon = updatedAddons.FirstOrDefault(updatedAddon =>
                    (updatedAddon.Name, GetAuthorsKey(updatedAddon.Authors)) == authorsKey);
                if (updateAddon != null)
                    updateAddon.CommonAddonId = addon.Id;
            }

            foreach (var dependency in dependencies)
            {
                var insertedAddon = toInsert.FirstOrDefault(commonAddon => commonAddon.Name == dependency.Name);
                if (insertedAddon != null)
                    dependency.Id = insertedAddon.Id;
            }
        }

        if (toUpdate.Count != 0)
            db.UpdateAll(toUpdate);
    }

    private static void RefreshLocalAddonDependencies(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var existingDependencies = db.Table<LocalAddonDependency>()
            .Select(dependency => (dependency.Id, dependency.LocalAddonId))
            .Distinct()
            .ToList();

        var updatedLocalAddonIds = updatedAddons.Select(addon => addon.LocalAddonId).ToList();
        var toDeleteIds = existingDependencies
            .Where(ed => !updatedLocalAddonIds.Contains(ed.LocalAddonId))
            .Select(ed => (object?)ed.Id)
            .ToList();

        db.DeleteAllIds<LocalAddonDependency>(toDeleteIds);

        UpdateLocalAddonDependencies(db, updatedAddons);
    }

    private static void RefreshOnlineAddonDependencies(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var existingDependencies = db.Table<OnlineAddonDependency>()
            .Select(dependency => (dependency.Id, dependency.OnlineAddonId))
            .Distinct()
            .ToList();

        var updatedOnlineAddonIds = updatedAddons.Select(addon => addon.OnlineAddonId).ToList();
        var toDeleteIds = existingDependencies
            .Where(ed => !updatedOnlineAddonIds.Contains(ed.OnlineAddonId))
            .Select(ed => (object?)ed.Id)
            .ToList();

        db.DeleteAllIds<OnlineAddonDependency>(toDeleteIds);

        UpdateOnlineAddonDependencies(db, updatedAddons);
    }

    private static void UpdateLocalAddonDependencies(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var toDeleteIds = new List<object>();
        var toInsert = new List<LocalAddonDependency>();

        foreach (var updatedAddon in updatedAddons)
        {
            if (updatedAddon.LocalAddonId == null)
                throw new NullReferenceException($"{updatedAddon.Name}: Local addon id is null");

            var existingDependencies = db.Table<LocalAddonDependency>()
                .Where(dependency => dependency.LocalAddonId == updatedAddon.LocalAddonId)
                .ToList();
            var updatedDependencyIds = updatedAddon.LocalDependencies.Select(dependency => dependency.Id).ToHashSet();

            toDeleteIds.AddRange(existingDependencies
                .Where(ed => !updatedDependencyIds.Contains(ed.DependentCommonAddonId))
                .Select(ed => (object)ed.Id)
                .ToList());

            var existingDependencyIds = existingDependencies.Select(ed => ed.DependentCommonAddonId).ToHashSet();
            toInsert.AddRange(updatedAddon.LocalDependencies
                .Where(d => !existingDependencyIds.Contains(d.Id))
                .Select(d => new LocalAddonDependency
                {
                    LocalAddonId = updatedAddon.LocalAddonId.Value,
                    DependentCommonAddonId = d.Id
                }).ToList());
        }

        if (toDeleteIds.Count != 0)
            db.DeleteAllIds<LocalAddonDependency>(toDeleteIds);

        if (toInsert.Count != 0)
            db.InsertAll(toInsert);
    }

    private static void UpdateOnlineAddonDependencies(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var toDeleteIds = new List<object>();
        var toInsert = new List<OnlineAddonDependency>();

        foreach (var updatedAddon in updatedAddons)
        {
            if (updatedAddon.OnlineAddonId == null)
                throw new NullReferenceException($"{updatedAddon.Name}: Local addon id is null");

            var existingDependencies = db.Table<OnlineAddonDependency>()
                .Where(dependency => dependency.OnlineAddonId == updatedAddon.OnlineAddonId)
                .ToList();
            var currentDependencyIds = updatedAddon.OnlineDependencies.Select(dependency => dependency.Id).ToHashSet();

            toDeleteIds.AddRange(existingDependencies
                .Where(ed => !currentDependencyIds.Contains(ed.DependentCommonAddonId))
                .Select(ed => (object)ed.Id)
                .ToList());

            var existingDependencyIds = existingDependencies.Select(ed => ed.DependentCommonAddonId).ToHashSet();
            toInsert.AddRange(updatedAddon.OnlineDependencies
                .Where(d => !existingDependencyIds.Contains(d.Id))
                .Select(d => new OnlineAddonDependency
                {
                    OnlineAddonId = updatedAddon.OnlineAddonId.Value,
                    DependentCommonAddonId = d.Id
                }).ToList());
        }

        if (toDeleteIds.Count != 0)
            db.DeleteAllIds<OnlineAddonDependency>(toDeleteIds);

        if (toInsert.Count != 0)
            db.InsertAll(toInsert);
    }

    private static bool UpdateLocalAddonFieldsIfChanged(LocalAddon localAddon, ILocalAddon updatedAddon)
    {
        var isChanged = false;

        if (localAddon.Version != updatedAddon.Version)
        {
            localAddon.Version = updatedAddon.Version;
            isChanged = true;
        }

        if (localAddon.DisplayedVersion != updatedAddon.DisplayedVersion)
        {
            localAddon.DisplayedVersion = updatedAddon.DisplayedVersion;
            isChanged = true;
        }

        if (localAddon.State != updatedAddon.State)
        {
            localAddon.State = updatedAddon.State;
            isChanged = true;
        }

        if (localAddon.InstallationMethod != updatedAddon.InstallationMethod)
        {
            localAddon.InstallationMethod = updatedAddon.InstallationMethod;
            isChanged = true;
        }

        return isChanged;
    }

    private static void RefreshLocalAddons(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var localAddons = db.Table<LocalAddon>().ToList();
        var toUpdate = new List<LocalAddon>();

        foreach (var addon in localAddons)
        {
            var updatedAddon = updatedAddons.FirstOrDefault(a => a.CommonAddonId == addon.CommonAddonId);
            if (updatedAddon != null)
            {
                if (UpdateLocalAddonFieldsIfChanged(addon, updatedAddon))
                    toUpdate.Add(addon);
                updatedAddon.LocalAddonId = addon.Id;
            }
            else
            {
                db.Delete(addon); // TODO delete LocalAddonDependency
            }
        }

        var localCommonAddonIds = new HashSet<int>(localAddons.Select(localAddon => localAddon.CommonAddonId));
        var toInsert = (from updatedAddon in updatedAddons
            where !localCommonAddonIds.Contains(updatedAddon.CommonAddonId)
            select new LocalAddon
            {
                CommonAddonId = updatedAddon.CommonAddonId,
                Version = updatedAddon.Version,
                DisplayedVersion = updatedAddon.DisplayedVersion,
                State = updatedAddon.State,
                InstallationMethod = updatedAddon.InstallationMethod
            }).ToList();

        if (toUpdate.Count != 0)
            db.UpdateAll(toUpdate);

        if (toInsert.Count != 0)
        {
            db.InsertAll(toInsert);

            foreach (var addon in toInsert)
            {
                var updateAddon = updatedAddons.FirstOrDefault(a => a.CommonAddonId == addon.CommonAddonId);
                if (updateAddon != null)
                    updateAddon.LocalAddonId = addon.Id;
                else Debug.WriteLine($"updated addon not found, commonid = {addon.CommonAddonId}");
            }
        }
    }

    private static bool UpdateOnlineAddonFieldsIfChanged(OnlineAddon onlineAddon, IOnlineAddon updatedAddon)
    {
        var isChanged = false;

        if (onlineAddon.LatestVersion != updatedAddon.LatestVersion)
        {
            onlineAddon.LatestVersion = updatedAddon.LatestVersion;
            isChanged = true;
        }

        if (onlineAddon.DisplayedLatestVersion != updatedAddon.DisplayedLatestVersion)
        {
            onlineAddon.DisplayedLatestVersion = updatedAddon.DisplayedLatestVersion;
            isChanged = true;
        }

        return isChanged;
    }

    private static void RefreshOnlineAddons(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var onlineAddons = db.Table<OnlineAddon>().ToList();
        var onlineAddonsToUpdate = new List<OnlineAddon>();

        foreach (var addon in onlineAddons)
        {
            var updatedAddon = updatedAddons.FirstOrDefault(a => a.CommonAddonId == addon.CommonAddonId);
            if (updatedAddon != null)
            {
                if (UpdateOnlineAddonFieldsIfChanged(addon, updatedAddon))
                    onlineAddonsToUpdate.Add(addon);
                updatedAddon.OnlineAddonId = addon.Id;
            }
            else
            {
                db.Delete(addon); //TODO delete OnlineAddonDependency
            }
        }

        var onlineCommonAddonIds = new HashSet<int>(onlineAddons.Select(onlineAddon => onlineAddon.CommonAddonId));
        var toInsert = (from updatedAddon in updatedAddons
            where !onlineCommonAddonIds.Contains(updatedAddon.CommonAddonId)
            select new OnlineAddon
            {
                CommonAddonId = updatedAddon.CommonAddonId,
                LatestVersion = updatedAddon.LatestVersion,
                DisplayedLatestVersion = updatedAddon.DisplayedLatestVersion,
                UniqueId = updatedAddon.UniqueId
            }).ToList();

        if (onlineAddonsToUpdate.Count != 0)
            db.UpdateAll(onlineAddonsToUpdate);

        if (toInsert.Count != 0)
        {
            db.InsertAll(toInsert);

            foreach (var addon in toInsert)
            {
                var updateAddon = updatedAddons.FirstOrDefault(a => a.CommonAddonId == addon.CommonAddonId);
                if (updateAddon != null)
                    updateAddon.OnlineAddonId = addon.Id;
            }
        }
    }

    private static void RemoveUnusedCommonAddons(EsoDataConnection db)
    {
        var usedCommonAddonIds = new HashSet<int>(
            db.Table<LocalAddon>().Select(localAddon => localAddon.CommonAddonId)
                .Union(db.Table<OnlineAddon>().Select(onlineAddon => onlineAddon.CommonAddonId))
        );

        var unusedCommonAddons = db.Table<CommonAddon>()
            .Where(ca => !usedCommonAddonIds.Contains(ca.Id))
            .ToList();

        var addonAuthorIdsToDelete = new List<object?>();
        var addonCategoryIdsToDelete = new List<object?>();
        foreach (var commonAddon in unusedCommonAddons)
        {
            addonAuthorIdsToDelete.AddRange(db.Table<AddonAuthor>()
                .Where(addonAuthor => addonAuthor.CommonAddonId == commonAddon.Id)
                .Select(addonAuthor => (object?)addonAuthor.Id)
                .ToList());

            addonCategoryIdsToDelete.AddRange(db.Table<AddonCategory>()
                .Where(addonCategory => addonCategory.CommonAddonId == commonAddon.Id)
                .Select(addonCategory => (object?)addonCategory.Id)
                .ToList());

            db.Delete(commonAddon);
        }

        if (addonAuthorIdsToDelete.Count > 0)
            db.DeleteAllIds<AddonAuthor>(addonAuthorIdsToDelete);

        if (addonCategoryIdsToDelete.Count > 0)
            db.DeleteAllIds<AddonCategory>(addonCategoryIdsToDelete);
    }

    private static void InsertOrUpdateAuthors(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var authors = db.GetAllWithChildren<Author>().ToList().ToDictionary(author => author.Name, author => author);

        foreach (var addon in updatedAddons)
        {
            var commonAddon = db.GetWithChildren<CommonAddon>(addon.CommonAddonId);
            commonAddon.Authors = [];

            var toInsert = new List<Author>();

            foreach (var addonAuthor in addon.Authors)
            {
                var lowerAuthorName = addonAuthor.Name.ToLower();
                if (!authors.TryGetValue(lowerAuthorName, out var author))
                {
                    author = new Author { Name = lowerAuthorName };
                    toInsert.Add(author);
                    authors[author.Name] = author;
                }

                commonAddon.Authors.Add(author);
            }

            if (toInsert.Count != 0)
                db.InsertAll(toInsert);

            db.UpdateWithChildren(commonAddon);
        }
    }

    private static void InsertOrUpdateCategories(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var categories = db.GetAllWithChildren<Category>().ToList().ToDictionary(category => category.Name, category => category);

        foreach (var addon in updatedAddons)
        {
            var commonAddon = db.GetWithChildren<CommonAddon>(addon.CommonAddonId);
            commonAddon.Categories = [];

            var toInsert = new List<Category>();

            foreach (var addonCategory in addon.Categories)
            {
                if (!categories.TryGetValue(addonCategory.Name, out var category))
                {
                    category = new Category { Name = addonCategory.Name };
                    toInsert.Add(category);
                    categories[category.Name] = category;
                }

                commonAddon.Categories.Add(category);
            }

            if (toInsert.Count != 0)
                db.InsertAll(toInsert);

            db.UpdateWithChildren(commonAddon);
        }
    }

    private static void RemoveUnusedAuthors(EsoDataConnection db)
    {
        var unusedAuthors = db.GetAllWithChildren<Author>().Where(a => a.CommonAddons.Count == 0).ToList();

        foreach (var author in unusedAuthors)
            db.Delete(author);
    }

    private static void RemoveUnusedCategories(EsoDataConnection db)
    {
        var unusedCategories = db.GetAllWithChildren<Category>().Where(a => a.CommonAddons.Count == 0).ToList();

        foreach (var category in unusedCategories)
            db.Delete(category);
    }

    public static void InsertOrUpdateLocalAddon(EsoDataConnection db, Addon updatedAddon)
    {
        InsertOrUpdateLocalAddons(db, [updatedAddon]);
    }

    public static void InsertOrUpdateLocalAddons(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var toUpdate = new List<LocalAddon>();
        var toInsert = new List<LocalAddon>();

        foreach (var addon in updatedAddons)
        {
            var existingLocalAddonId = db.Table<LocalAddon>()
                .Where(localAddon => localAddon.CommonAddonId == addon.CommonAddonId)
                .Select(localAddon => localAddon.Id)
                .FirstOrDefault();

            addon.LocalAddonId = existingLocalAddonId;
            var localAddon = addon.ToLocalAddon();

            if (existingLocalAddonId != null)
                toUpdate.Add(localAddon);
            else
                toInsert.Add(localAddon);

            if (!InstalledAddons.Contains(addon))
                InstalledAddons.Add(addon);
        }

        if (toUpdate.Count > 0)
            db.UpdateAll(toUpdate);

        if (toInsert.Count > 0)
        {
            db.InsertAll(toInsert);

            foreach (var localAddon in toInsert)
            {
                var installedAddon =
                    InstalledAddons.First(addon => addon.CommonAddonId == localAddon.CommonAddonId);
                installedAddon.LocalAddonId = localAddon.Id;
            }
        }

        UpdateLocalAddonDependencies(db, updatedAddons);
    }

    public static void RemoveLocalAddon(EsoDataConnection db, Addon updatedAddon)
    {
        RemoveLocalAddons(db, [updatedAddon]);
    }

    public static void RemoveLocalAddons(EsoDataConnection db, List<Addon> updatedAddons)
    {
        var commonAddonIds = updatedAddons.Select(addon => addon.CommonAddonId).ToList();
        var localAddonIdsToDelete = db.Table<LocalAddon>()
            .Where(localAddon => commonAddonIds.Contains(localAddon.CommonAddonId))
            .Select(localAddon => (object?)localAddon.Id)
            .ToList();
        if (localAddonIdsToDelete.Count > 0)
            db.DeleteAllIds<LocalAddon>(localAddonIdsToDelete);

        var localAddonDependencyIdsToDelete = db.Table<LocalAddonDependency>()
            .Where(addonDependency => localAddonIdsToDelete.Contains(addonDependency.LocalAddonId))
            .Select(addonDependency => (object?)addonDependency.Id)
            .ToList();
        if (localAddonDependencyIdsToDelete.Count > 0)
            db.DeleteAllIds<LocalAddonDependency>(localAddonDependencyIdsToDelete);

        InstalledAddons.RemoveAll(addon => commonAddonIds.Contains(addon.CommonAddonId));
    }
}
