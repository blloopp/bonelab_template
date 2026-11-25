using System;
using System.Collections.Generic;
using System.Linq;
using BoneLib;
using BoneLib.BoneMenu;
using Il2CppSLZ.Marrow.Pool;
using Il2CppSLZ.Marrow.Warehouse;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(QuickSpawnerMod.Main), "QuickSpawner", "1.0.0", "YourName")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace QuickSpawnerMod
{
    public class Main : MelonMod
    {
        private static Page _mainPage;
        private static Page _listPage;
        private static List<SpawnableCrate> _allCrates = new List<SpawnableCrate>();
        private static List<SpawnableCrate> _filteredCrates = new List<SpawnableCrate>();
        private static int _currentPage = 0;
        private const int ItemsPerPage = 8;

        public override void OnInitializeMelon()
        {
            // Create the main BoneMenu page
            _mainPage = Page.Root.CreatePage("Quick Spawner", Color.cyan);

            // Search bar using BoneLib's built-in VR keyboard
            _mainPage.CreateString("Search Item", Color.yellow, "", (searchText) =>
            {
                FilterCrates(searchText);
            });

            _mainPage.CreateFunction("Refresh & Show All", Color.green, () =>
            {
                LoadAllCrates();
                FilterCrates("");
            });

            _listPage = _mainPage.CreatePage("Browse Items", Color.white);
        }

        private static void LoadAllCrates()
        {
            _allCrates.Clear();

            if (AssetWarehouse.Instance == null) return;

            // Grab every spawnable crate from base game and installed mods
            foreach (var crate in AssetWarehouse.Instance.GetCrates())
            {
                var spawnable = crate.TryCast<SpawnableCrate>();
                if (spawnable != null && !string.IsNullOrEmpty(spawnable.Title))
                {
                    _allCrates.Add(spawnable);
                }
            }

            _allCrates = _allCrates.OrderBy(c => c.Title).ToList();
        }

        private static void FilterCrates(string query)
        {
            if (_allCrates.Count == 0)
            {
                LoadAllCrates();
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                _filteredCrates = new List<SpawnableCrate>(_allCrates);
            }
            else
            {
                string lowerQuery = query.ToLower();
                _filteredCrates = _allCrates
                    .Where(c => c.Title.ToLower().Contains(lowerQuery) || 
                                c.Barcode.ID.ToLower().Contains(lowerQuery))
                    .ToList();
            }

            _currentPage = 0;
            RebuildListPage();
        }

        private static void RebuildListPage()
        {
            _listPage.RemoveAll();

            if (_filteredCrates.Count == 0)
            {
                _listPage.CreateFunction("No items found. Hit Refresh!", Color.red, () => { });
                return;
            }

            int totalPages = Mathf.CeilToInt((float)_filteredCrates.Count / ItemsPerPage);
            _listPage.CreateFunction($"Page {_currentPage + 1} of {totalPages} ({_filteredCrates.Count} items)", Color.gray, () => { });

            // Navigation buttons
            if (_currentPage > 0)
            {
                _listPage.CreateFunction("< Previous Page", Color.yellow, () =>
                {
                    _currentPage--;
                    RebuildListPage();
                });
            }

            if ((_currentPage + 1) * ItemsPerPage < _filteredCrates.Count)
            {
                _listPage.CreateFunction("Next Page >", Color.yellow, () =>
                {
                    _currentPage++;
                    RebuildListPage();
                });
            }

            // Display items for the current page
            var pageItems = _filteredCrates
                .Skip(_currentPage * ItemsPerPage)
                .Take(ItemsPerPage);

            foreach (var crate in pageItems)
            {
                SpawnableCrate targetCrate = crate;
                _listPage.CreateFunction(targetCrate.Title, Color.white, () =>
                {
                    SpawnInFrontOfPlayer(targetCrate);
                });
            }
        }

        private static void SpawnInFrontOfPlayer(SpawnableCrate crate)
        {
            if (Player.Head == null) return;

            // Spawn 1.5 meters in front of where you are looking
            Vector3 forward = Player.Head.forward;
            forward.y = 0;
            forward.Normalize();

            Vector3 spawnPos = Player.Head.position + (forward * 1.5f);
            Quaternion spawnRot = Quaternion.LookRotation(-forward);

            // Uses BONELAB's native AssetSpawner, which LabFusion automatically intercepts and syncs
            var spawnable = new Il2CppSLZ.Marrow.Data.Spawnable()
            {
                crateRef = new SpawnableCrateReference(crate.Barcode)
            };

            AssetSpawner.Register(spawnable);
            AssetSpawner.Spawn(spawnable, spawnPos, spawnRot, new Il2CppSystem.Nullable<Vector3>(), null, false, new Il2CppSystem.Nullable<int>(), null, null);
        }
    }
}
