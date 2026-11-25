using System;
using System.Collections.Generic;
using System.Linq;
using BoneLib;
using BoneLib.BoneMenu;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using Il2CppSLZ.Marrow.Data;
using Il2CppSLZ.Marrow.Pool;
using Il2CppSLZ.Marrow.Warehouse;
using LabFusion.Entities;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.Representation;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BlloopsSpecialMod.Main), "blloops Special", "1.0.0", "blloop")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace BlloopsSpecialMod
{
    public class Main : MelonMod
    {
        // Feature Toggles
        public static bool AimAssistEnabled = false;
        public static bool InfiniteAmmoEnabled = false;
        public static bool NoclipEnabled = false;
        public static bool EspEnabled = false;
        public static bool SuperStrengthEnabled = false;
        public static bool SuperSpeedEnabled = false;
        public static bool FlashlightEnabled = false;

        // Adjustable Values
        public static float NoclipSpeed = 12f;
        public static float SpeedMultiplier = 2.5f;
        public static float StrengthMultiplier = 10f;

        // Internal References
        private static Light _flashlightObj;
        private static float _espTimer = 0f;
        private static float _defaultAgility = -1f;
        private static float _defaultStrengthUpper = -1f;
        private static float _defaultStrengthLower = -1f;

        // Quick Spawner References
        private static Page _spawnerPage;
        private static Page _listPage;
        private static List<SpawnableCrate> _allCrates = new List<SpawnableCrate>();
        private static List<SpawnableCrate> _filteredCrates = new List<SpawnableCrate>();
        private static int _currentPage = 0;
        private const int ItemsPerPage = 8;

        public override void OnInitializeMelon()
        {
            ClassInjector.RegisterTypeInIl2Cpp<EspHighlighter>();

            // Create the "blloops Special" BoneMenu Page
            Page mainPage = Page.Root.CreatePage("blloops Special", Color.magenta);

            // --- Combat & Movement Cheats ---
            mainPage.CreateBool("Aim Assist (NPC & Fusion)", Color.red, false, (val) =>
            {
                AimAssistEnabled = val;
            });

            mainPage.CreateBool("Infinite Ammo", Color.yellow, false, (val) =>
            {
                InfiniteAmmoEnabled = val;
            });

            mainPage.CreateBool("Noclip (Right Thumbstick)", Color.cyan, false, (val) =>
            {
                SetNoclip(val);
            });

            mainPage.CreateFloat("Noclip Fly Speed", Color.cyan, 12f, 2f, 2f, 40f, (val) =>
            {
                NoclipSpeed = val;
            });

            mainPage.CreateBool("ESP (NPC & Fusion)", Color.green, false, (val) =>
            {
                EspEnabled = val;
                if (!val) ClearAllEsp();
            });

            mainPage.CreateBool("Super Strength", new Color(1f, 0.5f, 0f), false, (val) =>
            {
                SuperStrengthEnabled = val;
                ApplyAvatarStats();
            });

            mainPage.CreateBool("Super Speed", new Color(0.4f, 0.8f, 1f), false, (val) =>
            {
                SuperSpeedEnabled = val;
                ApplyAvatarStats();
            });

            mainPage.CreateBool("Head Flashlight", Color.white, false, (val) =>
            {
                ToggleFlashlight(val);
            });

            // --- Fusion-Synced Quick Spawner Sub-Page ---
            _spawnerPage = mainPage.CreatePage("Quick Spawner (Fusion Sync)", Color.cyan);

            _spawnerPage.CreateString("Search Item", Color.yellow, "", (searchText) =>
            {
                FilterCrates(searchText);
            });

            _spawnerPage.CreateFunction("Refresh & Show All", Color.green, () =>
            {
                LoadAllCrates();
                FilterCrates("");
            });

            _listPage = _spawnerPage.CreatePage("Browse Items", Color.white);
        }

        public override void OnUpdate()
        {
            if (Player.RigManager == null) return;

            // 1. Noclip Flight Movement
            if (NoclipEnabled && Player.PhysicsRig != null && Player.Head != null)
            {
                HandleNoclipMovement();
            }

            // 2. Keep Flashlight attached to Head
            if (FlashlightEnabled && _flashlightObj != null && Player.Head != null)
            {
                _flashlightObj.transform.position = Player.Head.position + (Player.Head.forward * 0.12f);
                _flashlightObj.transform.rotation = Player.Head.rotation;
            }

            // 3. Refresh ESP Boxes every 0.5 seconds
            if (EspEnabled)
            {
                _espTimer += Time.deltaTime;
                if (_espTimer >= 0.5f)
                {
                    _espTimer = 0f;
                    UpdateEspTargets();
                }
            }

            // 4. Maintain Avatar Strength & Speed
            if (SuperStrengthEnabled || SuperSpeedEnabled)
            {
                ApplyAvatarStats();
            }
        }

        // ================= NOCLIP =================
        private static void SetNoclip(bool enabled)
        {
            NoclipEnabled = enabled;
            if (Player.PhysicsRig == null) return;

            var colliders = Player.RigManager.GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                if (col != null)
                    col.enabled = !enabled;
            }

            var rigidbodies = Player.RigManager.GetComponentsInChildren<Rigidbody>(true);
            foreach (var rb in rigidbodies)
            {
                if (rb != null)
                    rb.useGravity = !enabled;
            }
        }

        private static void HandleNoclipMovement()
        {
            Vector2 moveInput = Vector2.zero;
            if (Player.RightController != null)
                moveInput = Player.RightController.GetThumbStickAxis();

            if (moveInput.sqrMagnitude > 0.05f)
            {
                Vector3 flyDir = (Player.Head.forward * moveInput.y) + (Player.Head.right * moveInput.x);
                Vector3 moveStep = flyDir * NoclipSpeed * Time.deltaTime;

                Player.PhysicsRig.transform.position += moveStep;
                if (Player.PhysicsRig.m_chest != null)
                {
                    Player.PhysicsRig.m_chest.GetComponent<Rigidbody>().velocity = Vector3.zero;
                }
            }
            else if (Player.PhysicsRig.m_chest != null)
            {
                Player.PhysicsRig.m_chest.GetComponent<Rigidbody>().velocity = Vector3.zero;
            }
        }

        // ================= STATS & FLASHLIGHT =================
        private static void ApplyAvatarStats()
        {
            if (Player.Avatar == null) return;

            if (_defaultAgility < 0f)
            {
                _defaultAgility = Player.Avatar._agility;
                _defaultStrengthUpper = Player.Avatar._strengthUpper;
                _defaultStrengthLower = Player.Avatar._strengthLower;
            }

            Player.Avatar._agility = SuperSpeedEnabled ? (_defaultAgility * SpeedMultiplier) : _defaultAgility;
            Player.Avatar._strengthUpper = SuperStrengthEnabled ? (_defaultStrengthUpper * StrengthMultiplier) : _defaultStrengthUpper;
            Player.Avatar._strengthLower = SuperStrengthEnabled ? (_defaultStrengthLower * StrengthMultiplier) : _defaultStrengthLower;
        }

        private static void ToggleFlashlight(bool enabled)
        {
            FlashlightEnabled = enabled;

            if (enabled)
            {
                if (_flashlightObj == null)
                {
                    GameObject lightGo = new GameObject("BlloopsFlashlight");
                    GameObject.DontDestroyOnLoad(lightGo);
                    _flashlightObj = lightGo.AddComponent<Light>();
                    _flashlightObj.type = LightType.Spot;
                    _flashlightObj.range = 45f;
                    _flashlightObj.spotAngle = 65f;
                    _flashlightObj.intensity = 3.5f;
                    _flashlightObj.color = new Color(0.95f, 0.97f, 1f);
                }
                _flashlightObj.enabled = true;
            }
            else if (_flashlightObj != null)
            {
                _flashlightObj.enabled = false;
            }
        }

        // ================= ESP (NPC & FUSION) =================
        private static void UpdateEspTargets()
        {
            if (NetworkInfo.HasServer)
            {
                foreach (var playerId in PlayerIDManager.PlayerIDs)
                {
                    if (playerId == PlayerIDManager.LocalID) continue;

                    if (NetworkPlayerManager.TryGetPlayer(playerId.SmallID, out NetworkPlayer netPlayer))
                    {
                        if (netPlayer.HasRig && netPlayer.RigRefs.RigManager != null)
                        {
                            AttachEspMarker(netPlayer.RigRefs.RigManager.physicsRig.m_chest, new Color(0.75f, 0.15f, 1f, 0.55f));
                        }
                    }
                }
            }

            var aiBrains = GameObject.FindObjectsOfType<AIBrain>();
            foreach (var brain in aiBrains)
            {
                if (brain != null && !brain.isDead)
                {
                    AttachEspMarker(brain.transform, new Color(1f, 0.15f, 0.15f, 0.55f));
                }
            }
        }

        private static void AttachEspMarker(Transform target, Color color)
        {
            if (target == null) return;
if (target.GetComponentInChildren() != null) return;
GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
GameObject.Destroy(box.GetComponent());
box.transform.SetParent(target, false);
box.transform.localPosition = Vector3.zero;
box.transform.localScale = new Vector3(0.55f, 1.35f, 0.55f);
Renderer rend = box.GetComponent();
Shader shader = Shader.Find("GUI/Text Shader");
if (shader == null) shader = Shader.Find("Sprites/Default");
Material mat = new Material(shader);
mat.color = color;
rend.material = mat;
box.AddComponent();
}
private static void ClearAllEsp()
{
var markers = GameObject.FindObjectsOfType();
foreach (var marker in markers)
{
if (marker != null)
GameObject.Destroy(marker.gameObject);
}
}
// ================= AIM ASSIST TARGETING =================
public static Transform FindClosestAimTarget(Vector3 muzzlePos, Vector3 muzzleForward)
{
Transform bestTarget = null;
float bestScore = float.MaxValue;
if (NetworkInfo.HasServer)
{
foreach (var playerId in PlayerIDManager.PlayerIDs)
{
if (playerId == PlayerIDManager.LocalID) continue;
if (NetworkPlayerManager.TryGetPlayer(playerId.SmallID, out NetworkPlayer netPlayer))
{
if (netPlayer.HasRig && netPlayer.RigRefs.RigManager != null)
{
Transform head = netPlayer.RigRefs.RigManager.physicsRig.m_head;
if (head != null)
{
float score = EvaluateTarget(muzzlePos, muzzleForward, head.position);
if (score < bestScore)
{
bestScore = score;
bestTarget = head;
}
}
}
}
}
}
var aiBrains = GameObject.FindObjectsOfType();
foreach (var brain in aiBrains)
{
if (brain != null && !brain.isDead)
{
Vector3 targetPos = brain.transform.position + Vector3.up * 1.1f;
float score = EvaluateTarget(muzzlePos, muzzleForward, targetPos);
if (score < bestScore)
{
bestScore = score;
bestTarget = brain.transform;
}
}
}
return bestTarget;
}
private static float EvaluateTarget(Vector3 origin, Vector3 forward, Vector3 targetPos)
{
Vector3 toTarget = targetPos - origin;
float dist = toTarget.magnitude;
if (dist > 80f) return float.MaxValue;
float angle = Vector3.Angle(forward, toTarget.normalized);
if (angle > 45f) return float.MaxValue;
return dist + (angle * 0.5f);
}
// ================= QUICK SPAWNER =================
private static void LoadAllCrates()
{
_allCrates.Clear();
if (AssetWarehouse.Instance == null) return;
foreach (var crate in AssetWarehouse.Instance.GetCrates())
{
var spawnable = crate.TryCast();
if (spawnable != null && !string.IsNullOrEmpty(spawnable.Title))
{
_allCrates.Add(spawnable);
}
}
_allCrates = _allCrates.OrderBy(c => c.Title).ToList();
}
private static void FilterCrates(string query)
{
if (_allCrates.Count == 0) LoadAllCrates();
if (string.IsNullOrWhiteSpace(query))
{
_filteredCrates = new List(_allCrates);
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
Vector3 forward = Player.Head.forward;
forward.y = 0;
forward.Normalize();
Vector3 spawnPos = Player.Head.position + (forward * 1.5f);
Quaternion spawnRot = Quaternion.LookRotation(-forward);
var spawnable = new Spawnable()
{
crateRef = new SpawnableCrateReference(crate.Barcode)
};
AssetSpawner.Register(spawnable);
AssetSpawner.Spawn(spawnable, spawnPos, spawnRot, new Il2CppSystem.Nullable(), null, false, new Il2CppSystem.Nullable(), null, null);
}
}
public class EspHighlighter : MonoBehaviour
{
public EspHighlighter(IntPtr ptr) : base(ptr) { }
}
[HarmonyPatch(typeof(Gun), nameof(Gun.Fire))]
public static class GunFirePatch
{
public static void Prefix(Gun __instance)
{
if (__instance == null) return;
if (Main.InfiniteAmmoEnabled)
{
__instance.isCharged = true;
if (__instance.MagazineState != null)
{
__instance.MagazineState.SetCartridge(0);
__instance.MagazineState.ammoCount = __instance.MagazineState.magazineData.rounds;
}
}
if (Main.AimAssistEnabled && __instance.firePoint != null)
{
Transform target = Main.FindClosestAimTarget(__instance.firePoint.position, __instance.firePoint.forward);
if (target != null)
{
Vector3 aimPos = target.GetComponent() != null
? target.position + Vector3.up * 1.1f
: target.position;
__instance.firePoint.LookAt(aimPos);
}
}
}
}
}
