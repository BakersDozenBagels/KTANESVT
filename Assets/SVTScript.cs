using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

//test tweaks modes
//test timer modifier
//test black and white
public class SVTScript : MonoBehaviour
{
    #region Patching
    private static bool s_harmed;

    private void Awake()
    {
        GetComponent<KMNeedyModule>().ResetDelayMin = 30f;
        GetComponent<KMNeedyModule>().ResetDelayMax = 180f;

        if (s_harmed)
            return;

        s_harmed = true;

        Harmony harm = new Harmony("BDB.SVT");
        s_timerType = AppDomain
            .CurrentDomain
            .GetAssemblies()
            .SelectMany(GetTypes)
            .First(t => t.Name == "TimerComponent");
        s_timerRateModifier = s_timerType.GetField("rateModifier", BindingFlags.NonPublic | BindingFlags.Instance);

        var meth = s_timerType.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        var trans = typeof(SVTScript).GetMethod("Transpile", BindingFlags.NonPublic | BindingFlags.Static);
        harm.Patch(meth, transpiler: new HarmonyMethod(trans));

        meth = s_timerType.GetMethod("SetRateModifier", BindingFlags.Public | BindingFlags.Instance);
        var pref = typeof(SVTScript).GetMethod("TimerChanged", BindingFlags.NonPublic | BindingFlags.Static);
        harm.Patch(meth, prefix: new HarmonyMethod(pref));

        meth = AppDomain
            .CurrentDomain
            .GetAssemblies()
            .SelectMany(GetTypes)
            .First(t => t.Name == "NeedyComponent")
            .GetMethod("ResetAndStart", BindingFlags.NonPublic | BindingFlags.Instance);
        trans = typeof(SVTScript).GetMethod("NeedyActivateTranspiler", BindingFlags.NonPublic | BindingFlags.Static);
        harm.Patch(meth, transpiler: new HarmonyMethod(trans) { after = new string[] { "BlackAndWhiteKTANE" } });

        s_needyTimerType = AppDomain
            .CurrentDomain
            .GetAssemblies()
            .SelectMany(GetTypes)
            .First(t => t.Name == "NeedyTimer");
        s_needyTimerField = s_needyTimerType.GetField("Display", BindingFlags.Public | BindingFlags.Instance);
        meth = s_needyTimerType
            .GetMethod("StopTimer", BindingFlags.Public | BindingFlags.Instance);
        trans = typeof(SVTScript).GetMethod("NeedyTimerTranspiler", BindingFlags.NonPublic | BindingFlags.Static);
        harm.Patch(meth, transpiler: new HarmonyMethod(trans));
    }

    private static IEnumerable<CodeInstruction> NeedyTimerTranspiler(IEnumerable<CodeInstruction> instr)
    {
        var il = instr.ToList();
        il.InsertRange(3, new CodeInstruction[] {
            new CodeInstruction(OpCodes.Ldarg_0, null),
            CodeInstruction.Call(typeof(SVTScript), "EditTimer")
        });
        return il;
    }

    private static bool EditTimer(bool orig, MonoBehaviour script)
    {
        if (script.transform.parent.GetComponent<SVTScript>() != null)
            return true;
        return orig;
    }

    private static IEnumerable<CodeInstruction> NeedyActivateTranspiler(IEnumerable<CodeInstruction> instr, ILGenerator generator, MethodBase baseMethod)
    {
        var info = Harmony.GetPatchInfo(baseMethod);
        if (info != null && info.Transpilers != null && info.Transpilers.Any(p => p != null && p.owner == "BlackAndWhiteKTANE"))
        {
            var meth = AppDomain
                .CurrentDomain
                .GetAssemblies()
                .SelectMany(GetTypes)
                .First(t => t.Name == "BWService")
                .GetMethod("CheckNeedySound", BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var il in instr)
            {
                yield return il;
                if (il.Calls(meth))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0, null);
                    yield return CodeInstruction.Call(typeof(SVTScript), "CheckNeedySound");
                    yield return new CodeInstruction(OpCodes.And, null);
                }
            }

            yield break;
        }

        // Remaining implementation copied from Black and White
        // https://github.com/BakersDozenBagels/BlackAndWhite/blob/7514f7268ccca391feda1c1618776d2c30f061e1/Assets/BWService.cs#L690

        List<CodeInstruction> instructions = instr.ToList();
        int i = 0;
        for (; i < instructions.Count; i++)
        {
            if (!instructions[i].Is(OpCodes.Ldstr, "needy_activated"))
            {
                yield return instructions[i];
                continue;
            }
            yield return new CodeInstruction(instructions[i + 1]).MoveLabelsFrom(instructions[i]);
            yield return CodeInstruction.Call(typeof(SVTScript), "CheckNeedySound");
            Label lbl = generator.DefineLabel();
            yield return new CodeInstruction(OpCodes.Brfalse, lbl);
            for (; i < instructions.Count; i++)
            {
                yield return instructions[i];
                if (instructions[i].opcode == OpCodes.Pop)
                    break;
            }
            yield return instructions[i + 1].WithLabels(lbl);
            i += 2;
            break;
        }
        for (; i < instructions.Count; i++)
            yield return instructions[i];
    }

    private static bool CheckNeedySound(MonoBehaviour inst)
    {
        KMNeedyModule n = inst.GetComponent<KMNeedyModule>();
        if (n && n.ModuleType.Equals("SVT"))
            return false;
        return true;
    }

    private static IEnumerable<Type> GetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null);
        }
        catch
        {
            return Type.EmptyTypes;
        }
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> il)
    {
        foreach (var i in il)
        {
            yield return i;
            if (i.opcode == OpCodes.Mul)
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0, null);
                yield return new CodeInstruction(OpCodes.Call, typeof(SVTScript).GetMethod("GetTotalMultiplier", BindingFlags.NonPublic | BindingFlags.Static));
                yield return i;
            }
        }
    }

    private static void TimerChanged()
    {
        BeforeTimerSpeedChanged();
    }

    private static float GetTotalMultiplier(MonoBehaviour timer)
    {
        return timer
            .transform
            .root
            .GetComponentsInChildren<SVTScript>()
            .Select(s => s.Multiplier)
            .Aggregate(1f, (a, b) => a * b);
    }
    #endregion

    private static Type s_timerType, s_needyTimerType;
    private static FieldInfo s_timerRateModifier, s_needyTimerField;
    private static PropertyInfo s_displayProperty, s_displayTimeProperty;
    private static int s_idc;
    private readonly int _id = ++s_idc;

    private float Multiplier { get; set; }
    private Coroutine _bleed;
    private float _startTime, _lastChangeTime, _cumulativeTimeLoss;

    private static event Action BeforeTimerSpeedChanged = () => { };

    private void Start()
    {
        Multiplier = 1;
        GetComponent<KMNeedyModule>().OnNeedyActivation += Begin;
        GetComponent<KMNeedyModule>().OnNeedyDeactivation += End;

        GetComponent<KMSelectable>().Children[0].OnInteract += Press;
        BeforeTimerSpeedChanged += TimerSpeedChange;
        StartCoroutine(EnableTimer());
    }

    private IEnumerator EnableTimer()
    {
        yield return null;
        yield return null;

        var disp = s_needyTimerField.GetValue(GetComponentInChildren(s_needyTimerType));
        (s_displayProperty ?? (s_displayProperty = disp.GetType().GetProperty("On", BindingFlags.Public | BindingFlags.Instance))).SetValue(disp, true, new object[0]);
        (s_displayTimeProperty ?? (s_displayTimeProperty = disp.GetType().GetProperty("DisplayValue", BindingFlags.Public | BindingFlags.Instance))).SetValue(disp, 99, new object[0]);
    }

    private void OnDestroy()
    {
        BeforeTimerSpeedChanged -= TimerSpeedChange;
    }

    private void TimerSpeedChange()
    {
        _cumulativeTimeLoss += Calculus();
        _lastChangeTime = Time.time;
    }

    private bool Press()
    {
        GetComponent<KMSelectable>().Children[0].AddInteractionPunch();
        GetComponent<KMAudio>().PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.Stamp, GetComponent<KMSelectable>().Children[0].transform);
        End();
        return false;
    }

    private void Begin()
    {
        Debug.Log("[Supraventricular Tachycardia #" + _id + "] Module activated.");
        _bleed = StartCoroutine(Bleed());
    }

    const float AccelerationRate = 1f / 120f; // Units: change in timer multiplier per second
    private IEnumerator Bleed()
    {
        float startTime = _startTime = _lastChangeTime = Time.time;
        _cumulativeTimeLoss = 0f;
        var n = GetComponent<KMNeedyModule>();
        while (true)
        {
            Multiplier = 1f + (Time.time - startTime) * AccelerationRate;
            n.SetNeedyTimeRemaining(99f);
            yield return null;
        }
    }

    private void End()
    {
        Multiplier = 1f;
        if (_bleed != null)
        {
            StopCoroutine(_bleed);
            _bleed = null;
            Debug.Log("[Supraventricular Tachycardia #" + _id + "] Module deactivated. You lost " + (_cumulativeTimeLoss + Calculus()) + " seconds (bomb time).");
            GetComponent<KMNeedyModule>().HandlePass();
        }
    }

    private float Calculus()
    {
        float x = Time.time - _startTime;
        float y = _lastChangeTime - _startTime;
        float baseRate = (float)s_timerRateModifier.GetValue(transform.root.GetComponentInChildren(s_timerType));
        return Mathf.Abs(baseRate * (x * x - y * y) * AccelerationRate / 2f);
    }
}
