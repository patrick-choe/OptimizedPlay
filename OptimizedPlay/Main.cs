/*
 * Copyright (C) 2021 PatrickKR
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ADOFAI;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityModManagerNet;
using Object = UnityEngine.Object;

namespace OptimizedPlay
{
	internal static class Main
	{
		// 하모니 (트윅)
		private static Harmony _harmony;

		// 모드
		private static UnityModManager.ModEntry _mod;

		// 디버그용 숫자
		private static long _init;

		// 테스트용
		private static bool _shouldSkip;
		private static bool _shouldRender;

		// 모드 로드
		private static bool Load(UnityModManager.ModEntry modEntry)
		{
			_mod = modEntry;
			_mod.OnToggle = OnToggle;

			return true;
		}

		// 트윅 토글
		private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
		{
			_mod = modEntry;

			if (value) StartTweaks();
			else StopTweaks();

			return true;
		}

		// 트윅 켜기
		private static void StartTweaks()
		{
			_harmony = new Harmony(_mod.Info.Id);
			_harmony.PatchAll(Assembly.GetExecutingAssembly());
		}

		// 트윅 끄기
		private static void StopTweaks()
		{
			_harmony.UnpatchAll(_harmony.Id);
		}

		// 노래 멈춰!
		[HarmonyPatch(typeof(scnEditor), "SwitchToEditMode")]
		private static class EditorSwitchToEditMode
		{
			private static void Prefix(ADOBase __instance)
			{
				__instance.conductor.song.Stop();
				AudioManager.Instance.StopAllSounds();
				_shouldRender = true;
			}
		}

		// 누가 날 불렀니?
		[HarmonyPatch(typeof(scrController), "ResetCustomLevel")]
		private static class ControllerResetCustomLevel
		{
			private static void Prefix()
			{
				_init = DateTimeOffset.Now.ToUnixTimeMilliseconds();
				_shouldSkip = true;
			}
		}

		// 그만좀 이벤트 적용해 ㅡㅡ
		// [HarmonyPatch(typeof(CustomLevel), "ApplyEventsToFloors", typeof(List<scrFloor>), typeof(LevelData), typeof(scrLevelMaker), typeof(List<LevelEvent>))]
		// private static class ApplyEventsToFloors
		// {
		// 	private static void Postfix()
		// 	{
		// 		// 이벤트 멈춰!
		// 		_mod.Logger.Log("event stop!");
		// 	}
		// }

		// scnEditor Play 오버라이딩
		[HarmonyPatch(typeof(scnEditor), "Play")]
		private static class EditorPlayPatch
		{
			private static bool Prefix(scnEditor __instance, ICollection<GameObject> ___floorNumGOs, ICollection<GameObject> ___floorConnectorGOs, ref scrFloor ___lastSelectedFloor)
			{
				if (GCS.standaloneLevelMode) return true;
				_init = DateTimeOffset.Now.ToUnixTimeMilliseconds();
				if (__instance.customLevel.levelMaker.listFloors.Count == 1)
					return false;
				var seqID = __instance.selectedFloor == null ? 0 : __instance.selectedFloor.seqID;
				__instance.selectedFloorCached = seqID;
				EventSystem.current.SetSelectedGameObject(null);
				if (__instance.selectedFloor != null || __instance.multiSelectedFloors.Count != 0)
				{
					__instance.SaveState(onlySelectionChanged: true);
					++__instance.editor.changingState;
					DOTween.Kill("selectedColorTween", true);
					foreach (var floor in __instance.customLevel.levelMaker.listFloors)
					{
						// r71 이후용
						floor.floorRenderer.color = Color.white;
						// r68 전용
						// floor.floorsprite.DOKill(true);
						// floor.floorsprite.color = Color.white;
					}
					__instance.levelEventsPanel.HideAllInspectorTabs();
					__instance.selectedFloor = null;
					__instance.multiSelectedFloors.Clear();
					if (___lastSelectedFloor != null)
					{
						___lastSelectedFloor = null;
						__instance.OnSelectedFloorChange();
					}
					--__instance.editor.changingState;
				}
				__instance.customLevel.ReloadAssets();
				// __instance.customLevel.RemakePath();
				scrConductor.instance.countdownTicks = __instance.levelData.countdownTicks;
				foreach (var floorNumGo in ___floorNumGOs)
					Object.Destroy(floorNumGo);
				___floorNumGOs.Clear();
				foreach (var floorConnectorGo in ___floorConnectorGOs)
					Object.Destroy(floorConnectorGo);
				___floorConnectorGOs.Clear();
				__instance.controller.currentSeqID = 0;
				GCS.checkpointNum = seqID;
				__instance.customLevel.Play(seqID);
				return false;
			}
		}

		// CustomLevel Play 오버라이딩
		[HarmonyPatch(typeof(CustomLevel), "Play")]
		private static class CustomLevelPlayPatch
		{
			private static bool Prefix(CustomLevel __instance, ref bool __result, int seqID = 0)
			{
				if (GCS.standaloneLevelMode) return true;
				if (scrLevelMaker.instance.listFloors.Count == 1)
				{
					__result = true;

					return false;
				}
				if (seqID != 0)
					++__instance.checkpointsUsed;
				var instance1 = scrController.instance;
				var instance2 = scrConductor.instance;
				var fullCaptionTagged = __instance.levelData.fullCaptionTagged;
				var redPlanet = instance1.redPlanet;
				var bluePlanet = instance1.bluePlanet;
				instance1.caption = fullCaptionTagged;
				instance1.stickToFloor = __instance.levelData.stickToFloors;
				instance1.chosenplanet = redPlanet;
				if (__instance.isLevelEditor)
				{
					redPlanet.Rewind();
					redPlanet.transform.localPosition = Vector3.zero;
					redPlanet.shouldPrint = true;
					bluePlanet.Rewind();
					bluePlanet.transform.localPosition = Vector3.right;
					bluePlanet.shouldPrint = true;
				}
				AudioManager.Instance.StopAllSounds();
				scrCamera.instance.Rewind();
				__instance.camParent.transform.position = Vector3.zero;
				instance2.Rewind();
				instance1.Awake_Rewind();
				var instance3 = scrCamera.instance;
				instance3.zoomSize = 1f;
				if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
					instance3.userSizeMultiplier = 1f;
				scrVfxPlus.instance.vidOffset = (int)__instance.levelData.miscSettings.data["vidOffset"] * (1f / 1000f);
				var floor = scrLevelMaker.instance.listFloors[seqID];
				instance1.isCW = !floor.isCCW;
				instance1.speed = floor.speed;
				instance1.rotationEase = floor.planetEase;
				instance1.chosenplanet = floor.seqID % 2 == 0 ? redPlanet : bluePlanet;
				instance1.keyBufferCount = 0;
				__instance.conductor.bpm = __instance.levelData.bpm;
				__instance.conductor.crotchet = 60.0 / __instance.conductor.bpm;
				__instance.conductor.crotchetAtStart = __instance.conductor.crotchet;
				__instance.conductor.addoffset = __instance.levelData.offset * (1.0 / 1000.0);
				__instance.conductor.song.volume = __instance.levelData.volume * 0.01f;
				__instance.conductor.hitSoundVolume = __instance.levelData.hitsoundVolume * 0.01f;
				__instance.conductor.hitSound = __instance.levelData.hitsound;
				__instance.conductor.separateCountdownTime = __instance.levelData.separateCountdownTime;
				var num = __instance.levelData.pitch * 0.01f;
				if (GCS.standaloneLevelMode)
					num *= GCS.currentSpeedRun;
				__instance.conductor.song.pitch = num;
				if (__instance.isLevelEditor)
				{
					var flag = RDC.auto && seqID == 0;
					instance2.fastTakeoff = flag;
					instance1.forceNoCountdown = flag;
				}
				instance1.txtAllStrictClear.gameObject.SetActive(false);
				instance1.txtCongrats.gameObject.SetActive(false);
				instance1.txtPercent.gameObject.SetActive(false);
				if (GCS.standaloneLevelMode)
				{
					__instance.StartCoroutine(instance1.WaitForStartCo(seqID));
				}
				else
				{
					instance2.Start();
					instance1.Start_Rewind(seqID);
					__instance.ApplyEventsToFloors(scrLevelMaker.instance.listFloors);
					__instance.UpdateVideo();
					if (!GCS.standaloneLevelMode)
						__instance.PrepVfx(seqID == 0);
					redPlanet.LoadPlanetColor();
					bluePlanet.LoadPlanetColor();
					if (GCS.standaloneLevelMode)
						Persistence.IncrementCustomWorldAttempts(__instance.levelData.Hash);
					__instance.controller.mistakesManager.hitMargins = new List<HitMargin>();
					__instance.controller.currentSeqID = 0;
					__instance.controller.mistakesManager.CalculatePercentAcc();
				}
				__result = true;

				_mod.Logger.Log("total time: " + (DateTimeOffset.Now.ToUnixTimeMilliseconds() - _init));
				return false;
			}
		}

		// CustomLevel ResetScene 오버라이딩
		[HarmonyPatch(typeof(CustomLevel), "ResetScene")]
		private static class CustomLevelResetScenePatch
		{
			private static bool Prefix(CustomLevel __instance)
			{
				if (GCS.standaloneLevelMode) return true;
				__instance.isLoading = true;
				scrUIController.instance.txtCountdown.GetComponent<scrCountdown>().CancelGo();
				__instance.controller.perfectEffects.Clear();
				__instance.controller.hitEffects.Clear();
				__instance.controller.barelyEffects.Clear();
				__instance.controller.missEffects.Clear();
				__instance.controller.lossEffects.Clear();
				__instance.ReloadAssets();
				var instance = scrCamera.instance;
				var transform = instance.transform;
				transform.localPosition = transform.position;
				transform.rotation = Quaternion.identity;
				__instance.camParent.transform.position = Vector3.zero;
				instance.followMode = true;
				instance.zoomSize = 1f;
				instance.shake = Vector3.zero;
				instance.Bgcamstatic.enabled = true;
				instance.Bgcamstatic.backgroundColor = __instance.levelData.backgroundColor;
				instance.GetComponent<VideoBloom>().enabled = false;
				// r71 start
				instance.GetComponent<ScreenTile>().enabled = false;
				instance.GetComponent<ScreenScroll>().enabled = false;
				// r71 end
				__instance.DisableFilters();
				__instance.SetStartingBG();
				if (__instance.videoBG.isPlaying)
					__instance.videoBG.Stop();
				__instance.ResetPlanetsPosition();
				switch (GCS.standaloneLevelMode)
				{
					case false when !scrController.instance.paused:
					case true when scrController.instance.paused:
						__instance.controller.TogglePauseGame();
						break;
				}
				foreach (var @object in GameObject.FindGameObjectsWithTag("MissIndicator"))
					Object.Destroy(@object);
				__instance.controller.missesOnCurrFloor.Clear();
				foreach (var typingLetter in __instance.controller.typingLetters)
					Object.Destroy(typingLetter.gameObject);
				__instance.controller.typingLetters.Clear();
				DOTween.KillAll();
				GameObject.Find("FlashPlus").GetComponent<Renderer>().material.color = Color.clear;
				scrVfxPlus.instance.Reset();
				scrVfxPlus.instance.camAngle = 0.0f;
				scrVfxPlus.instance.controllingCam = false;
				if (!_shouldSkip)
				{
					__instance.RemakePath();
				}
				else
				{
					_shouldSkip = false;

					scrLevelMaker.instance.MakeLevel();

					if (_shouldRender)
					{
						_shouldRender = false;
						CustomLevel.ApplyEventsToFloors(scrLevelMaker.instance.listFloors, __instance.levelData, __instance.lm, __instance.events);
					}
					else
					{
						var multiplier = 1f;
						var twirl = false;
						var offset = Vector2.zero;
						foreach (var floor1 in scrLevelMaker.instance.listFloors)
						{
							var floor = floor1;
							foreach (var levelEvent in __instance.events.FindAll(x => x.floor == floor.seqID))
							{
								switch (levelEvent.eventType)
								{
									case LevelEventType.PositionTrack:
										if (!levelEvent.data.Keys.Contains("editorOnly") || (Toggle)levelEvent.data["editorOnly"] != Toggle.Enabled)
											offset += RDUtils.StringToVector2(levelEvent.data["positionOffset"].ToString()) * 1.5f;
										continue;
									case LevelEventType.SetSpeed when (SpeedType)levelEvent.data["speedType"] == SpeedType.Bpm:
										multiplier = levelEvent.GetFloat("beatsPerMinute") / __instance.levelData.bpm;
										continue;
									case LevelEventType.SetSpeed:
										multiplier *= levelEvent.GetFloat("bpmMultiplier");
										continue;
									case LevelEventType.Twirl:
										twirl = !twirl;
										continue;
									case LevelEventType.Checkpoint:
										floor.gameObject.AddComponent<ffxCheckpoint>();
										continue;
								}
							}
							floor.transform.position = floor.startPos + new Vector3(offset.x, offset.y, 0.0f);
							floor.offsetPos = new Vector3(offset.x, offset.y, 0.0f);
							floor.speed = multiplier;
							floor.isCCW = twirl;
						}
					}
				}

				return false;
			}
		}
	}
}
