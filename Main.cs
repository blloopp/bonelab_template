using System;
using System.Collections.Generic;
using BoneLib;
using Il2CppSLZ.Marrow;
using LabFusion.Entities;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.Representation;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(FusionWristTP.Main), "FusionWristTP", "1.0.0", "VibeCoder")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace FusionWristTP
{
    public class Main : MelonMod
    {
        private static GameObject _menuRoot;
        private static Transform _buttonContainer;
        private static bool _isMenuOpen = false;
        private static float _lastTapTime = 0f;
        private static bool _wasTouchingWrist = false;

        // Colors for your Purple & Black theme
        private static readonly Color DarkBlack = new Color(0.06f, 0.06f, 0.08f, 0.95f);
        private static readonly Color NeonPurple = new Color(0.55f, 0.15f, 0.95f, 1f);
        private static readonly Color ButtonPurple = new Color(0.22f, 0.08f, 0.38f, 0.95f);

        public override void OnUpdate()
        {
            if (Player.RigManager == null || Player.RightHand == null || Player.LeftHand == null)
                return;

            // 1. Keep the wrist anchor attached to your right wrist
            if (_menuRoot == null)
            {
                BuildWristMenu();
            }

            Transform rightWrist = Player.RightHand.transform;
            _menuRoot.transform.position = rightWrist.position + (rightWrist.up * 0.08f) + (rightWrist.forward * -0.04f);
            _menuRoot.transform.rotation = rightWrist.rotation * Quaternion.Euler(45f, 180f, 0f);

            // 2. Detect double-tap on your right wrist with your left hand
            float distToWrist = Vector3.Distance(Player.LeftHand.transform.position, rightWrist.position);
            bool isTouchingNow = distToWrist < 0.085f;

            if (isTouchingNow && !_wasTouchingWrist)
            {
                float timeSinceLastTap = Time.time - _lastTapTime;
                if (timeSinceLastTap > 0.08f && timeSinceLastTap < 0.45f)
                {
                    ToggleMenu();
                    _lastTapTime = 0f;
                }
                else
                {
                    _lastTapTime = Time.time;
                }
            }
            _wasTouchingWrist = isTouchingNow;

            // 3. Detect left hand poking a player button while menu is open
            if (_isMenuOpen && _buttonContainer != null)
            {
                CheckButtonPoke(Player.LeftHand.transform.position);
            }
        }

        private static void ToggleMenu()
        {
            _isMenuOpen = !_isMenuOpen;
            _menuRoot.SetActive(_isMenuOpen);

            if (_isMenuOpen)
            {
                RefreshPlayerList();
            }
        }

        private static void BuildWristMenu()
        {
            _menuRoot = new GameObject("FusionWristTP_Menu");
            GameObject.DontDestroyOnLoad(_menuRoot);

            Canvas canvas = _menuRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rootRect = _menuRoot.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(260f, 340f);
            _menuRoot.transform.localScale = Vector3.one * 0.00075f;

            Image bg = _menuRoot.AddComponent<Image>();
            bg.color = DarkBlack;

            Outline border = _menuRoot.AddComponent<Outline>();
            border.effectColor = NeonPurple;
            border.effectDistance = new Vector2(5f, 5f);

            // Header Bar
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(_menuRoot.transform, false);
            Image headerImg = headerObj.AddComponent<Image>();
            headerImg.color = NeonPurple;

            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 0.88f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.offsetMin = Vector2.zero;
            headerRect.offsetMax = Vector2.zero;

            CreateText(headerObj.transform, "FUSION TELEPORT", 20, Color.white);

            // Player List Container
            GameObject listObj = new GameObject("PlayerList");
            listObj.transform.SetParent(_menuRoot.transform, false);
            _buttonContainer = listObj.transform;

            RectTransform listRect = listObj.GetComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0.05f, 0.04f);
            listRect.anchorMax = new Vector2(0.95f, 0.85f);
            listRect.offsetMin = Vector2.zero;
            listRect.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = listObj.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            _menuRoot.SetActive(false);
        }

        private static void RefreshPlayerList()
        {
            if (_buttonContainer == null) return;

            for (int i = _buttonContainer.childCount - 1; i >= 0; i--)
            {
                GameObject.Destroy(_buttonContainer.GetChild(i).gameObject);
            }

            if (!NetworkInfo.HasServer)
            {
                CreatePlayerButton("Not in a Fusion Server", 255);
                return;
            }

            int count = 0;
            foreach (var playerId in PlayerIdManager.PlayerIds)
            {
                // Skip yourself
                if (playerId == PlayerIdManager.LocalId) continue;

                string displayName = playerId.Metadata.Nickname.GetValue();
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = $"Player {playerId.SmallId}";

                CreatePlayerButton(displayName, playerId.SmallId);
                count++;
            }

            if (count == 0)
            {
                CreatePlayerButton("No other players in lobby", 255);
            }
        }

        private static void CreatePlayerButton(string label, byte targetSmallId)
        {
            GameObject btnObj = new GameObject("PlayerBtn_" + label);
            btnObj.transform.SetParent(_buttonContainer, false);

            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220f, 36f);

            Image img = btnObj.AddComponent<Image>();
            img.color = targetSmallId != 255 ? ButtonPurple : new Color(0.15f, 0.15f, 0.15f, 0.8f);

            Outline btnOutline = btnObj.AddComponent<Outline>();
            btnOutline.effectColor = NeonPurple;
            btnOutline.effectDistance = new Vector2(2f, 2f);

            CreateText(btnObj.transform, label, 16, Color.white);

            if (targetSmallId != 255)
            {
                var tag = btnObj.AddComponent<TeleportTargetTag>();
                tag.TargetSmallId = targetSmallId;
            }
        }

        private static void CheckButtonPoke(Vector3 leftHandPos)
        {
            for (int i = 0; i < _buttonContainer.childCount; i++)
            {
                Transform child = _buttonContainer.GetChild(i);
                var tag = child.GetComponent<TeleportTargetTag>();
                if (tag == null || tag.TargetSmallId == 255) continue;

                float dist = Vector3.Distance(leftHandPos, child.position);
                if (dist < 0.032f && Time.time - tag.LastClickTime > 0.6f)
                {
                    tag.LastClickTime = Time.time;
                    TeleportToFusionPlayer(tag.TargetSmallId);
                }
            }
        }

        private static void TeleportToFusionPlayer(byte targetSmallId)
        {
            if (NetworkPlayerManager.TryGetPlayer(targetSmallId, out NetworkPlayer netPlayer))
            {
                if (netPlayer.HasRig && netPlayer.RigRefs.RigManager != null)
                {
                    Transform targetChest = netPlayer.RigRefs.RigManager.physicsRig.m_chest;
                    Vector3 targetPos = targetChest.position + (targetChest.forward * -0.8f);

                    Player.RigManager.Teleport(targetPos, true);
                    ToggleMenu();
                }
            }
        }

        private static void CreateText(Transform parent, string content, int fontSize, Color color)
        {
            GameObject textObj = new GameObject("Label");
            textObj.transform.SetParent(parent, false);

            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 0f);
            rect.offsetMax = new Vector2(-8f, 0f);

            Text txt = textObj.AddComponent<Text>();
            txt.text = content;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = color;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class TeleportTargetTag : MonoBehaviour
    {
        public TeleportTargetTag(IntPtr ptr) : base(ptr) { }
        public byte TargetSmallId;
        public float LastClickTime;
    }
}
