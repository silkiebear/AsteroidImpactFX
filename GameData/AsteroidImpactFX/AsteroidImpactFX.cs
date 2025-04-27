using System;
using System.Collections;
using UnityEngine;
using KSP;

[KSPScenario(ScenarioCreationOptions.AddToAllGames, typeof(AsteroidImpactFX))]
public class AsteroidImpactFX : ScenarioModule
{
    // Settings
    private float effectDuration = 120f;
    private float effectScale = 1000f;
    private float lightRange = 25000f;
    private float lightIntensity = 8f;
    private float lightDuration = 3f;
    private bool enableSound = true;
    private float soundRange = 50000f;
    private float soundVolume = 1.0f;
    private double lookAheadTime = 300.0;
    private double minAsteroidMass = 0.1;
    private bool debugLogging = false;

    private bool armed;                            // blocker for double-spawns

    public override void OnAwake()
    {
        try
        {
            Debug.Log("[AsteroidImpactFX] Initializing...");
            LoadSettings();
            base.OnAwake();
        }
        catch (Exception e)
        {
            Debug.LogError($"[AsteroidImpactFX] Error in OnAwake: {e.Message}");
        }
    }

    private void LoadSettings()
    {
        try
        {
            ConfigNode settingsNode = GameDatabase.Instance.GetConfigNode("AsteroidImpactFX/Settings");
            if (settingsNode != null)
            {
                settingsNode.TryGetValue("EffectDuration", ref effectDuration);
                settingsNode.TryGetValue("EffectScale", ref effectScale);
                settingsNode.TryGetValue("LightRange", ref lightRange);
                settingsNode.TryGetValue("LightIntensity", ref lightIntensity);
                settingsNode.TryGetValue("LightDuration", ref lightDuration);
                settingsNode.TryGetValue("EnableSound", ref enableSound);
                settingsNode.TryGetValue("SoundRange", ref soundRange);
                settingsNode.TryGetValue("SoundVolume", ref soundVolume);
                settingsNode.TryGetValue("LookAheadTime", ref lookAheadTime);
                settingsNode.TryGetValue("MinAsteroidMass", ref minAsteroidMass);
                settingsNode.TryGetValue("DebugLogging", ref debugLogging);

                if (debugLogging)
                {
                    Debug.Log("[AsteroidImpactFX] Settings loaded:");
                    Debug.Log($"[AsteroidImpactFX] EffectDuration: {effectDuration}");
                    Debug.Log($"[AsteroidImpactFX] EffectScale: {effectScale}");
                    Debug.Log($"[AsteroidImpactFX] LightRange: {lightRange}");
                    Debug.Log($"[AsteroidImpactFX] LightIntensity: {lightIntensity}");
                    Debug.Log($"[AsteroidImpactFX] LightDuration: {lightDuration}");
                    Debug.Log($"[AsteroidImpactFX] EnableSound: {enableSound}");
                    Debug.Log($"[AsteroidImpactFX] SoundRange: {soundRange}");
                    Debug.Log($"[AsteroidImpactFX] SoundVolume: {soundVolume}");
                    Debug.Log($"[AsteroidImpactFX] LookAheadTime: {lookAheadTime}");
                    Debug.Log($"[AsteroidImpactFX] MinAsteroidMass: {minAsteroidMass}");
                }
            }
            else
            {
                Debug.LogWarning("[AsteroidImpactFX] Settings file not found, using defaults");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AsteroidImpactFX] Error loading settings: {e.Message}");
        }
    }

    public void FixedUpdate()
    {
        try
        {
            foreach (var v in FlightGlobals.Vessels)
            {
                if (v.vesselType != VesselType.SpaceObject || v.packed) continue;
                if (v.GetTotalMass() < minAsteroidMass) continue;

                double dt = v.orbit.GetDTforAltitude(0, 0);
                if (double.IsNaN(dt) || dt > lookAheadTime) continue;

                double impactUT = Planetarium.GetUniversalTime() + dt;
                Vector3d relPos  = v.orbit.getRelativePositionAtUT(impactUT);
                Vector3d pos     = v.mainBody.position + relPos;
                v.orbit.GetLatLonAltAtUT(impactUT, out double lat, out double lon, out _);

                if (debugLogging)
                {
                    Debug.Log($"[AsteroidImpactFX] Detected impact on {v.mainBody.name} at lat: {lat:F2}, lon: {lon:F2}");
                }
                StartCoroutine(SpawnImpact(v.mainBody, lat, lon, pos));
                v.Die();                                               // delete the asteroid
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AsteroidImpactFX] Error in FixedUpdate: {e.Message}");
        }
    }

    private IEnumerator SpawnImpact(CelestialBody body, double lat, double lon, Vector3d worldPos)
    {
        if (armed) yield break;                                   // one impact at a time
        armed = true;
        yield return new WaitForEndOfFrame();                     // let vessel despawn first

        try
        {
            // Create an empty GameObject fixed to the surface
            GameObject anchor = new GameObject("AI_FX_anchor");
            anchor.transform.parent = body.transform;
            anchor.transform.position = body.GetWorldSurfacePosition(lat, lon, 0);

            // Spawn Waterfall effect
            var template = Waterfall.WaterfallEffectTemplates.GetEffect("AsteroidImpact");
            if (template != null)
            {
                var fx = template.Spawn(anchor.transform.position, Quaternion.identity, anchor.transform);
                fx.transform.localScale *= effectScale;
                if (debugLogging)
                {
                    Debug.Log($"[AsteroidImpactFX] Spawned explosion effect at {lat:F2}, {lon:F2}");
                }
            }
            else
            {
                Debug.LogWarning("[AsteroidImpactFX] Could not find Waterfall effect template 'AsteroidImpact'");
            }

            // Simple light-flash glow
            Light flash = anchor.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = lightRange;
            flash.intensity = lightIntensity;
            UnityEngine.Object.Destroy(flash, lightDuration);

            // Add sound effect with distance-based attenuation
            if (enableSound)
            {
                AudioSource impactSound = anchor.AddComponent<AudioSource>();
                impactSound.clip = GameDatabase.Instance.GetAudioClip("AsteroidImpactFX/Sounds/impact");
                impactSound.spatialBlend = 1.0f;  // Full 3D sound
                impactSound.maxDistance = soundRange;
                impactSound.volume = soundVolume;
                impactSound.Play();
                UnityEngine.Object.Destroy(impactSound, impactSound.clip.length);
            }

            // Despawn anchor later
            UnityEngine.Object.Destroy(anchor, effectDuration + 1f);
        }
        catch (Exception e)
        {
            Debug.LogError($"[AsteroidImpactFX] Error in SpawnImpact: {e.Message}");
        }
        finally
        {
            armed = false;
        }
    }
} 