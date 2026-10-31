using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// Milestone 4: dynamic split screen.
    ///
    /// The game keeps exactly one camera tagged MainCamera, which every
    /// Camera.main world/screen conversion depends on. We therefore leave the
    /// main camera in place and only change its viewport rect. Up to three
    /// additional untagged cameras render the other viewports.
    ///
    /// While split, the game's CameraFollowTarget is disabled and all cameras
    /// are driven from here. While merged, it is re-enabled and the original
    /// rect is restored, so vanilla camera behaviour is untouched.
    ///
    /// All game types are resolved by name; Unity types are referenced
    /// normally because UnityEngine.Modules supplies compile-time stubs.
    /// </summary>
    internal static class SplitScreenFeature
    {
        internal const string Id = "dynamic-split-screen";

        internal static bool Active { get; private set; }
        internal static string Reason { get; private set; }

        private static FieldInfo _playersField;
        private static FieldInfo _cameraFollowInstance;
        private static FieldInfo _inConversationField;
        private static FieldInfo _inMenuField;
        private static MethodInfo _forceSnapTo;

        private static readonly List<Camera> Clones = new List<Camera>();
        private static Camera _main;
        private static Behaviour _cameraFollow;
        private static bool _cameraFollowWasEnabled;
        private static Rect _originalRect = new Rect(0f, 0f, 1f, 1f);
        private static bool _hasOriginalRect;
        private static bool _splitActive;
        private static bool _warnedNoCamera;

        private static Vector3 _cameraOffset;
        private static float _singlePlayerSize;
        private static float _transitionSeconds = 0.25f;
        private static SplitScreenMath.Mode _mode = SplitScreenMath.Mode.Merged;
        private static float _timeInMode;

        private static readonly List<SplitScreenMath.Vec2> Positions = new List<SplitScreenMath.Vec2>(4);
        private static SplitScreenMath.Vec2[] _positionsArray = new SplitScreenMath.Vec2[0];

        internal static void Install(bool cotlmpPresent)
        {
            Active = false;
            Reason = null;

            if (cotlmpPresent)
            {
                Reason = "COTLMP installed; split screen disabled";
                return;
            }

            try
            {
                Type playerFarming = AccessTools.TypeByName("PlayerFarming");
                if (playerFarming == null)
                {
                    Reason = "PlayerFarming type not found";
                    return;
                }

                _playersField = AccessTools.Field(playerFarming, "players");
                if (_playersField == null)
                {
                    Reason = "PlayerFarming.players not found";
                    return;
                }

                Type cameraFollow = AccessTools.TypeByName("CameraFollowTarget");
                if (cameraFollow != null)
                {
                    _cameraFollowInstance = AccessTools.Field(cameraFollow, "Instance");
                    _inConversationField = AccessTools.Field(cameraFollow, "IN_CONVERSATION");
                    _forceSnapTo = AccessTools.Method(cameraFollow, "ForceSnapTo");
                }

                Type gameManager = AccessTools.TypeByName("GameManager");
                if (gameManager != null)
                    _inMenuField = AccessTools.Field(gameManager, "InMenu");

                Active = true;
                Reason = cameraFollow == null
                    ? "CameraFollowTarget not found; split disabled"
                    : "camera director ready";

                if (cameraFollow == null)
                    Active = false;
            }
            catch (Exception e)
            {
                Active = false;
                Reason = "reflection failed: " + e.GetType().Name + ": " + e.Message;
                Plugin.Log.LogWarning("SplitScreen: " + Reason);
            }
        }

        /// <summary>
        /// Called from Plugin.LateUpdate, after gameplay movement and after the
        /// game's own camera logic would have run.
        /// </summary>
        internal static void Tick(ModConfig settings, float deltaTime)
        {
            if (!Active || settings == null)
                return;

            _transitionSeconds = Mathf.Max(0.05f, settings.CameraTransitionMs.Value / 1000f);

            if (!settings.EnableSplitScreen.Value)
            {
                Merge();
                return;
            }

            Camera main = Camera.main;
            if (main == null)
            {
                Merge();
                if (!_warnedNoCamera)
                {
                    _warnedNoCamera = true;
                    Plugin.Log.LogWarning("SplitScreen: no MainCamera yet; staying merged.");
                }
                return;
            }
            _warnedNoCamera = false;

            if (_main != main)
            {
                ForgetCameraState();
                _main = main;
            }

            ReadPositions(_playersField.GetValue(null) as IList);

            if (Positions.Count < 2)
            {
                if (Positions.Count == 1)
                    _singlePlayerSize = main.orthographicSize;
                Merge();
                return;
            }

            if (IsConversation() || IsInMenu())
            {
                Merge();
                return;
            }

            if (_splitActive)
                EnsureCameraFollowDisabled();
            else
            {
                // Keep the saved rect current while merged (letterboxing and
                // scripted camera changes can alter it between splits).
                _originalRect = main.rect;
                _hasOriginalRect = true;

                // Common case: everyone is together. Skip the allocator.
                float splitThreshold = Mathf.Max(
                    settings.MergeDistance.Value + 0.01f, settings.SplitDistance.Value);
                if (_mode == SplitScreenMath.Mode.Merged && CurrentSpread() < splitThreshold)
                    return;
            }

            if (!_splitActive)
                BeginSplit(main);

            CopyPositions();
            SplitScreenMath.Config config = BuildConfig(main, settings);
            SplitScreenMath.Plan plan = SplitScreenMath.Solve(
                _positionsArray, _mode, _timeInMode, config);

            Apply(plan, main, deltaTime);

            if (plan.Mode == _mode)
            {
                _timeInMode += Mathf.Max(0f, deltaTime);
            }
            else
            {
                _mode = plan.Mode;
                _timeInMode = 0f;
            }
        }

        internal static void Shutdown()
        {
            Merge();
            DestroyClones();
            _main = null;
            Active = false;
        }

        private static void ReadPositions(IList players)
        {
            Positions.Clear();
            if (players == null)
                return;

            for (int i = 0; i < players.Count && Positions.Count < 4; i++)
            {
                Component player = players[i] as Component;
                if (player == null)
                    continue;
                if (!player.gameObject.activeInHierarchy)
                    continue;

                Vector3 position = player.transform.position;
                Positions.Add(new SplitScreenMath.Vec2(position.x, position.y));
            }
        }

        private static void CopyPositions()
        {
            if (_positionsArray.Length != Positions.Count)
                _positionsArray = new SplitScreenMath.Vec2[Positions.Count];

            for (int i = 0; i < Positions.Count; i++)
                _positionsArray[i] = Positions[i];
        }

        private static float CurrentSpread()
        {
            float best = 0f;
            for (int i = 0; i < Positions.Count; i++)
            {
                for (int j = i + 1; j < Positions.Count; j++)
                {
                    float dx = Positions[i].X - Positions[j].X;
                    float dy = Positions[i].Y - Positions[j].Y;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance > best)
                        best = distance;
                }
            }
            return best;
        }

        private static void EnsureCameraFollowDisabled()
        {
            if (_cameraFollow != null || _cameraFollowInstance == null)
                return;

            _cameraFollow = _cameraFollowInstance.GetValue(null) as Behaviour;
            if (_cameraFollow == null)
                return;

            _cameraFollowWasEnabled = _cameraFollow.enabled;
            _cameraFollow.enabled = false;
        }

        private static SplitScreenMath.Config BuildConfig(Camera main, ModConfig settings)
        {
            float baseline = _singlePlayerSize > 0.1f
                ? _singlePlayerSize
                : Mathf.Max(4f, main.orthographicSize * 0.75f);

            float merge = settings.MergeDistance.Value;
            float split = Mathf.Max(merge + 0.01f, settings.SplitDistance.Value);

            Rect baseRect = _hasOriginalRect ? _originalRect : new Rect(0f, 0f, 1f, 1f);
            float baseAspect = (baseRect.width * Screen.width)
                               / Mathf.Max(1f, baseRect.height * Screen.height);

            return new SplitScreenMath.Config
            {
                MergeDistance = merge,
                SplitDistance = split,
                Padding = Mathf.Max(0f, settings.CameraPadding.Value),
                MinSize = Mathf.Max(1f, baseline),
                MaxSize = Mathf.Max(baseline, settings.MaxSplitSize.Value),
                ScreenAspect = Mathf.Max(0.1f, baseAspect),
                DwellSeconds = Mathf.Max(0f, settings.CameraStateDwellMs.Value / 1000f)
            };
        }

        private static void BeginSplit(Camera main)
        {
            if (!_hasOriginalRect)
            {
                _originalRect = main.rect;
                _hasOriginalRect = true;
            }

            Vector3 centre = AveragePosition();
            _cameraOffset = main.transform.position - new Vector3(centre.x, centre.y, 0f);
            if (_cameraOffset.sqrMagnitude < 0.01f)
                _cameraOffset = new Vector3(0f, 0f, -10f);

            _cameraFollow = _cameraFollowInstance == null
                ? null
                : _cameraFollowInstance.GetValue(null) as Behaviour;

            if (_cameraFollow != null)
            {
                _cameraFollowWasEnabled = _cameraFollow.enabled;
                _cameraFollow.enabled = false;
            }

            _splitActive = true;
            _mode = SplitScreenMath.Mode.Merged;
            _timeInMode = 0f;
            Plugin.Log.LogInfo("SplitScreen: entering split mode.");
        }

        private static void Apply(SplitScreenMath.Plan plan, Camera main, float deltaTime)
        {
            int viewportCount = plan.Viewports.Length;
            float blend = 1f - Mathf.Exp(-deltaTime * (3f / Mathf.Max(0.05f, _transitionSeconds)));

            for (int i = 0; i < viewportCount; i++)
            {
                Camera camera = i == 0 ? main : EnsureClone(i - 1, main);
                if (camera == null)
                    continue;

                if (i > 0)
                {
                    camera.transform.rotation = main.transform.rotation;
                    camera.cullingMask = main.cullingMask;
                    camera.clearFlags = main.clearFlags;
                    camera.backgroundColor = main.backgroundColor;
                    camera.depth = main.depth + 0.1f * i;
                }

                Drive(camera, plan, i, blend);
            }

            for (int i = viewportCount - 1; i < Clones.Count; i++)
            {
                if (Clones[i] != null)
                    Clones[i].enabled = false;
            }

            // The math asks for a merged view: keep driving the main camera
            // until its rect is full again, then hand control back to the game.
            if (plan.Mode == SplitScreenMath.Mode.Merged
                && viewportCount == 1
                && RectClose(main.rect, ToScreenRect(plan.Viewports[0]), 0.01f))
            {
                Merge();
            }
        }

        private static void Drive(Camera camera, SplitScreenMath.Plan plan, int viewport, float blend)
        {
            camera.enabled = true;
            camera.rect = BlendRect(camera.rect, ToScreenRect(plan.Viewports[viewport]), blend);
            camera.orthographicSize = Mathf.Lerp(
                camera.orthographicSize, plan.Sizes[viewport], blend);

            Vector3 target = new Vector3(plan.Centers[viewport].X, plan.Centers[viewport].Y, 0f)
                             + _cameraOffset;
            camera.transform.position = Vector3.Lerp(camera.transform.position, target, blend);
        }

        private static Camera EnsureClone(int index, Camera main)
        {
            while (Clones.Count <= index)
                Clones.Add(null);

            if (Clones[index] == null)
                Clones[index] = CreateClone(main);

            return Clones[index];
        }

        private static Camera CreateClone(Camera main)
        {
            var gameObject = new GameObject("RemotePlayEnhanced Split Camera " + (Clones.Count + 1));
            gameObject.tag = "Untagged";

            var camera = gameObject.AddComponent<Camera>();
            camera.CopyFrom(main);
            camera.targetTexture = null;
            camera.depth = main.depth + 0.1f * (Clones.Count + 1);
            camera.rect = new Rect(0f, 0f, 0.001f, 0.001f);
            camera.transform.position = main.transform.position;
            camera.transform.rotation = main.transform.rotation;
            camera.enabled = false;
            return camera;
        }

        private static void Merge()
        {
            if (!_splitActive)
            {
                _mode = SplitScreenMath.Mode.Merged;
                return;
            }

            for (int i = 0; i < Clones.Count; i++)
            {
                if (Clones[i] != null)
                    Clones[i].enabled = false;
            }

            if (_main != null && _hasOriginalRect)
                _main.rect = _originalRect;

            // Hand the current position to the game's follower before it takes
            // over, so re-enabling does not snap the camera.
            if (_cameraFollow != null && _forceSnapTo != null && _main != null)
            {
                try
                {
                    _forceSnapTo.Invoke(_cameraFollow, new object[] { _main.transform.position });
                }
                catch (Exception)
                {
                    // A missing/renamed ForceSnapTo only costs a camera jump.
                }
            }

            if (_cameraFollow != null)
                _cameraFollow.enabled = _cameraFollowWasEnabled;

            _splitActive = false;
            _mode = SplitScreenMath.Mode.Merged;
            _timeInMode = 0f;
            Plugin.Log.LogInfo("SplitScreen: merged.");
        }

        private static void ForgetCameraState()
        {
            Merge();
            DestroyClones();
            _hasOriginalRect = false;
            _originalRect = new Rect(0f, 0f, 1f, 1f);
            _cameraFollow = null;
            _singlePlayerSize = 0f;
            _splitActive = false;
            _mode = SplitScreenMath.Mode.Merged;
            _timeInMode = 0f;
        }

        private static void DestroyClones()
        {
            for (int i = 0; i < Clones.Count; i++)
            {
                if (Clones[i] != null)
                    UnityEngine.Object.Destroy(Clones[i].gameObject);
            }
            Clones.Clear();
        }

        private static Vector3 AveragePosition()
        {
            if (Positions.Count == 0)
                return Vector3.zero;

            float x = 0f, y = 0f;
            for (int i = 0; i < Positions.Count; i++)
            {
                x += Positions[i].X;
                y += Positions[i].Y;
            }
            return new Vector3(x / Positions.Count, y / Positions.Count, 0f);
        }

        private static bool IsConversation()
        {
            if (_inConversationField == null || _cameraFollowInstance == null)
                return false;

            object instance = _cameraFollowInstance.GetValue(null);
            if (instance == null)
                return false;

            object value = _inConversationField.GetValue(instance);
            return value is bool && (bool)value;
        }

        private static bool IsInMenu()
        {
            if (_inMenuField == null)
                return false;
            object value = _inMenuField.GetValue(null);
            return value is bool && (bool)value;
        }

        private static Rect ToScreenRect(SplitScreenMath.Viewport viewport)
        {
            Rect baseRect = _hasOriginalRect ? _originalRect : new Rect(0f, 0f, 1f, 1f);
            return new Rect(
                baseRect.x + viewport.X * baseRect.width,
                baseRect.y + viewport.Y * baseRect.height,
                viewport.W * baseRect.width,
                viewport.H * baseRect.height);
        }

        private static Rect BlendRect(Rect from, Rect to, float blend)
        {
            return new Rect(
                Mathf.Lerp(from.x, to.x, blend),
                Mathf.Lerp(from.y, to.y, blend),
                Mathf.Lerp(from.width, to.width, blend),
                Mathf.Lerp(from.height, to.height, blend));
        }

        private static bool RectClose(Rect a, Rect b, float epsilon)
        {
            return Mathf.Abs(a.x - b.x) <= epsilon
                   && Mathf.Abs(a.y - b.y) <= epsilon
                   && Mathf.Abs(a.width - b.width) <= epsilon
                   && Mathf.Abs(a.height - b.height) <= epsilon;
        }
    }
}
