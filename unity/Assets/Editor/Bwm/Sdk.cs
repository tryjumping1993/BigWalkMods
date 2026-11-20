// Access to BigPuzzleSDK from build code. The SDK's editor classes are mostly internal or private, and its
// component fields are private [SerializeField]s, so everything goes through reflection and SerializedObject.
// That also keeps this project compiling if the SDK package is missing or mid-update.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bwm
{
    public static class Sdk
    {
        public const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();

        // An SDK type by its simple name, e.g. "PuzzlePegBoard" or "PuzzleExporterWindow"
        public static Type T(string simpleName)
        {
            if (types.TryGetValue(simpleName, out Type found)) return found;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!a.GetName().Name.StartsWith("BigPuzzleSDK")) continue;
                Type[] all;
                try { all = a.GetTypes(); } catch (ReflectionTypeLoadException e) { all = e.Types.Where(t => t != null).ToArray(); }
                foreach (Type t in all) if (t.Name == simpleName) return types[simpleName] = t;
            }
            throw new Exception("SDK type not found: " + simpleName + " (is BigPuzzleSDK.unitypackage imported, and is the name right?)");
        }

        public static object Call(Type type, object target, string method, params object[] args)
        {
            MethodInfo m = type.GetMethods(Any).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            if (m == null) throw new Exception("Method not found: " + type.Name + "." + method + "/" + args.Length);
            return m.Invoke(target, args);
        }

        public static Exception Unwrap(Exception e) => e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;

        public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // The SDK component of this type on the object or its children
        public static Component Get(GameObject go, string type)
        {
            Component c = go.GetComponentInChildren(T(type), true);
            if (c == null) throw new Exception("'" + go.name + "' has no " + type);
            return c;
        }

        // Sets a serialized field of an SDK component. Enums take their index (int); references take the object.
        public static void Set(Component c, string field, object value)
        {
            var so = new SerializedObject(c);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) throw new Exception(c.GetType().Name + " has no field '" + field + "'");
            switch (p.propertyType)
            {
                case SerializedPropertyType.Enum: p.enumValueIndex = Convert.ToInt32(value); break;
                case SerializedPropertyType.Integer: p.intValue = Convert.ToInt32(value); break;
                case SerializedPropertyType.Boolean: p.boolValue = (bool)value; break;
                case SerializedPropertyType.Float: p.floatValue = Convert.ToSingle(value); break;
                case SerializedPropertyType.String: p.stringValue = (string)value; break;
                case SerializedPropertyType.Color: p.colorValue = (Color)value; break;
                case SerializedPropertyType.Vector3: p.vector3Value = (Vector3)value; break;
                case SerializedPropertyType.ObjectReference: p.objectReferenceValue = (UnityEngine.Object)value; break;
                default: throw new Exception("Unsupported field type " + p.propertyType + " for " + c.GetType().Name + "." + field);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Appends objects to a serialized list/array of references (e.g. PuzzlePressInOrder.buttons)
        public static void Add(Component c, string field, params UnityEngine.Object[] items)
        {
            var so = new SerializedObject(c);
            SerializedProperty p = so.FindProperty(field);
            if (p == null || !p.isArray) throw new Exception(c.GetType().Name + " has no list '" + field + "'");
            foreach (UnityEngine.Object item in items)
            {
                p.InsertArrayElementAtIndex(p.arraySize);
                p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = item;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Makes source.<eventField> (a UnityEvent) call target.<method>() with no argument, as the Inspector's + button would
        // The target can be an SDK component, one of our own scripts, a built-in component or a GameObject. With
        // 'arg' the method takes that one argument (bool, int, float or string), e.g. GameObject.SetActive(true).
        public static void Wire(Component source, string eventField, UnityEngine.Object target, string method, object arg = null)
        {
            var so = new SerializedObject(source);
            SerializedProperty calls = so.FindProperty(eventField + ".m_PersistentCalls.m_Calls");
            if (calls == null) throw new Exception(source.GetType().Name + " has no event '" + eventField + "'");
            Type[] signature = arg == null ? Type.EmptyTypes : new[] { arg.GetType() };
            if (target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance, null, signature, null) == null)
                throw new Exception(target.GetType().Name + " has no public method " + method + "(" + (arg == null ? "" : arg.GetType().Name) + ")");
            calls.InsertArrayElementAtIndex(calls.arraySize);
            SerializedProperty call = calls.GetArrayElementAtIndex(calls.arraySize - 1);
            call.FindPropertyRelative("m_Target").objectReferenceValue = target;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = target.GetType().AssemblyQualifiedName;
            call.FindPropertyRelative("m_MethodName").stringValue = method;
            call.FindPropertyRelative("m_CallState").intValue = 2;     // UnityEventCallState.RuntimeOnly
            SerializedProperty args = call.FindPropertyRelative("m_Arguments");
            int mode = 1;                                              // PersistentListenerMode: 1 Void, 3 Int, 4 Float, 5 String, 6 Bool
            if (arg is bool flag) { mode = 6; args.FindPropertyRelative("m_BoolArgument").boolValue = flag; }
            else if (arg is int number) { mode = 3; args.FindPropertyRelative("m_IntArgument").intValue = number; }
            else if (arg is float real) { mode = 4; args.FindPropertyRelative("m_FloatArgument").floatValue = real; }
            else if (arg is string text) { mode = 5; args.FindPropertyRelative("m_StringArgument").stringValue = text; }
            else if (arg != null) throw new Exception("Unsupported event argument type " + arg.GetType().Name);
            call.FindPropertyRelative("m_Mode").intValue = mode;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
