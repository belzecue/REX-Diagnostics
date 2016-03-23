﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.CodeDom.Compiler;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Rex.Utilities.Helpers;

namespace Rex.Utilities
{
    public static class RexHelper
    {
        public class Varible
        {
            public object VarValue { get; set; }
            public Type VarType { get; set; }
        }
        public static readonly Dictionary<string, Varible> Variables = new Dictionary<string, Varible>();
        public static IEnumerable<AConsoleOutput> Output { get { return outputList; } }
        private static readonly LinkedList<AConsoleOutput> outputList = new LinkedList<AConsoleOutput>();
        const int OUTPUT_LENGHT = 20;

        public static readonly Dictionary<MsgType, List<string>> Messages = new Dictionary<MsgType, List<string>>()
        {
            { MsgType.None, new List<string>()},
            { MsgType.Info, new List<string>()},
            { MsgType.Warning, new List<string>()},
            { MsgType.Error, new List<string>()}
        };

        #region Execute
        /// <summary>
        /// Sees to it that the toggle gets executed correctly.
        /// </summary>
        /// <param name="toggleInfo"></param>
        /// <returns></returns>
        public static IEnumerator ToggleExecution<T>(ToggleExecution<T> toggleInfo, Action<ToggleExecution<T>> toggleDone)
            where T : AConsoleOutput, new()
        {
            while (toggleInfo.KeepGoing)
            {
                bool showMemebers = true, showDetails = true;

                if (toggleInfo.Result != null)
                {
                    showMemebers = toggleInfo.Result.ShowMembers;
                    showDetails = toggleInfo.Result.ShowDetails;
                }
                var result = Execute<T>(toggleInfo.Compile, false);

                toggleInfo.Result = result;
                toggleInfo.Result.ShowMembers = showMemebers;
                toggleInfo.Result.ShowDetails = showDetails;

                toggleInfo.CurrentExecuteCount++;
                if (toggleInfo.MaxExecuteCount > 0)
                {
                    toggleInfo.KeepGoing = toggleInfo.MaxExecuteCount >= toggleInfo.CurrentExecuteCount;
                }
                if (toggleInfo.Type == ToggleType.OnceAFrame)
                    yield return 0;
                else
                    yield return toggleInfo.yeildWait;
            }
            toggleDone(toggleInfo);
        }

        public static T Execute<T>(CompiledExpression compileResult, bool showMessages = true)
            where T : AConsoleOutput, new()
        {
            try
            {
                var val = Utils.ExecuteAssembly(compileResult.Assembly);


                // If this is a variable declaration
                if (compileResult.Parse.IsDeclaring)
                {
                    DeclaringVariable(compileResult.Parse.Variable, val, showMessages);
                }

                // If expression is void
                if (compileResult.FuncType == FuncType._void)
                {
                    var outp = new T();
                    outp.LoadInDetails(null, "Expression successfully executed.", Enumerable.Empty<MemberDetails>());
                    return outp;
                }
                // If expression is null
                if (val == null)
                {
                    var outp = new T();
                    outp.LoadInDetails(null, "null", Enumerable.Empty<MemberDetails>());
                    return outp;
                }

                // Get the type of the variable
                var type = val.GetType();
                var message = val.ToString();
                if (!Utils.IsToStringOverride(type))
                {
                    message = Utils.GetCSharpRepresentation(type, true).ToString();
                }

                var output = new T();
                output.LoadInDetails(val, message, Logger.ExtractDetails(val));
                if (!(val is string || val is Enum) && val is IEnumerable)
                {
                    foreach (object o in (val as IEnumerable))
                    {
                        var member = new T();
                        var msg = o == null ? "null" : o.ToString();
                        member.LoadInDetails(o, msg, Logger.ExtractDetails(o));
                        output.Members.Add(member);
                    }
                }
                return output;
            }
            catch (Exception ex)
            {
                var exception = ex.InnerException == null ? ex : ex.InnerException;

                if (exception is AccessingDeletedVariableException)
                {
                    var deletedVar = exception as AccessingDeletedVariableException;
                    var msg = "Variable " + deletedVar.VarName + " has been deleted, but is still being accessed";
                    if (showMessages) Messages[MsgType.Warning].Add(msg);
                    return new T
                    {
                        Message = msg,
                        Exception = deletedVar
                    };
                }

                if (showMessages) Messages[MsgType.Error].Add(exception.ToString());
                return new T { Exception = exception };
            }
        }

        /// <summary>
        /// Handles a variable declaration.
        /// </summary>
        /// <param name="varName">Name of the variable</param>
        /// <param name="val">Value of the variable</param>
        /// <param name="showMessages">Should show an warning message or not</param>
        private static void DeclaringVariable(string varName, object val, bool showMessages)
        {
            var warning = string.Empty;
            if (val != null)
            {
                var valType = val.GetType();
                //if (valType.IsVisible)

                if (ContainsAnonymousType(valType))
                {
                    warning = string.Format("Cannot declare a variable '{0}' with anonymous type", varName);
                }
                else
                {
                    //var testCompile = Utils.CompileCode("cla/*ss rex_tmp { " + Utils.GetCSharpRepresentation(valType, true) + " myField; }");

                    //if (testCompile.Errors.Count > 0)
                    if (!valType.IsVisible)
                    {
                        var interfaces = valType.GetInterfaces();
                        var iEnumerable = interfaces.FirstOrDefault(t => t.IsGenericType && t.GetInterface("IEnumerable") != null);
                        if (iEnumerable != null)
                        {
                            Variables[varName] = new Varible { VarValue = val, VarType = iEnumerable };
                            return;
                        }
                        warning = string.Format("Expression returned a compiler generated class. Could not declare variable '{0}'", varName);
                    }
                    else
                    {
                        Variables[varName] = new Varible { VarValue = val, VarType = valType };
                        return;
                    }
                }
            }
            else
            {
                warning = string.Format("Expression returned null. Could not declare variable '{0}'", varName);
            }
            if (showMessages) Messages[MsgType.Warning].Add(warning);

        }

        public static bool ContainsAnonymousType(Type valType)
        {
            if (Logger.IsAnonymousType(valType))
                return true;
            if (valType.IsGenericType)
            {
                foreach (var genericType in valType.GetGenericArguments())
                {
                    if (ContainsAnonymousType(genericType)) return true;
                }
            }
            if (valType.IsArray)
            {
                if (ContainsAnonymousType(valType.GetElementType()))
                {
                    return true;
                }
            }
            return false;
        }
        #endregion
        #region Compile
        public static CompiledExpression Compile(
            ParseResult parseResult,
            Dictionary<string, HistoryItem> history)
        {
            if (history.ContainsKey(parseResult.WholeCode))
                return history[parseResult.WholeCode].Compile;

            var returnTypes = new[] { FuncType._object, FuncType._void };
            foreach (var type in returnTypes)
            {
                var wrapper = MakeWrapper(parseResult, type);

                var result = Utils.CompileCode(wrapper);

                if (DealWithErrors(result))
                {
                    if (type == FuncType._void)
                        Messages[MsgType.Error].Clear();

                    var compile = new CompiledExpression
                    {
                        Assembly = result.CompiledAssembly,
                        Parse = parseResult,

                        FuncType = type,
                    };
                    history.Add(parseResult.WholeCode, new HistoryItem { Compile = compile });
                    return compile;
                }
            }
            return null;
        }

        /// <summary>
        /// Outputs errors if there are any. returns true if there are none.
        /// </summary>
        public static bool DealWithErrors(CompilerResults result)
        {
            bool succsessful = true;
            foreach (CompilerError error in result.Errors)
            {
                if (!error.IsWarning)
                {
                    succsessful = false;
                    if (error.ErrorText.StartsWith("Cannot implicitly convert type"))
                        continue;

                    if (error.ErrorText.StartsWith("Only assignment, call, increment, decrement, and new object expressions") &&
                        Messages[MsgType.Error].Count > 0)
                        continue;

                    if (error.ErrorText.Trim().EndsWith("(Location of the symbol related to previous error)") &&
                        Messages[MsgType.Error].Count > 0)
                        continue;

                    if (!Messages[MsgType.Error].Contains(error.ErrorText))
                        Messages[MsgType.Error].Add(error.ErrorText);
                }
            }
            return succsessful;
        }

        public static string MakeWrapper(ParseResult parseResult, FuncType returnType)
        {
            var variableProps = Variables.Aggregate("", (codeString, var) =>
                codeString + Environment.NewLine +
                string.Format(@"    {0} {1} 
    {{ 
        get 
        {{
            if (!Rex.Utilities.RexHelper.Variables.ContainsKey(""{1}""))
                throw new Rex.Utilities.Helpers.AccessingDeletedVariableException() {{ VarName = ""{1}"" }};
            return ({0})Rex.Utilities.RexHelper.Variables[""{1}""].VarValue;
        }} 
        set {{ Rex.Utilities.RexHelper.Variables[""{1}""].VarValue = value; }} 
    }}",
            Utils.GetCSharpRepresentation(var.Value.VarType, true).ToString(), var.Key));

            var returnstring = parseResult.ExpressionString;
            if (!string.IsNullOrEmpty(parseResult.TypeString))
                returnstring = string.Format("({0})({1})", parseResult.TypeString, parseResult.ExpressionString);

            var baseWrapper = Utils.Usings + @"

class " + Utils.className + @"
{";

            if (parseResult.IsDeclaring || returnType == FuncType._object)
            {
                return baseWrapper + @"
        " + variableProps + @"
    public object " + Utils.FuncName + @"() 
    { 
        return " + returnstring + @";
    }
}";
            }
            else
            {
                return baseWrapper + @"
        " + variableProps + @"
    public void " + Utils.FuncName + @"() 
    { 
        " + returnstring + @";
    }
}";
            }

        }
        #endregion

        #region Parse
        public static ParseResult ParseAssigment(string parsedCode)
        {
            var match = Utils.Assgiment.Match(parsedCode);
            if (match.Success)
            {
                var type = match.Groups["type"].Value.Trim();
                var variable = match.Groups["var"].Value.Trim();
                var expr = match.Groups["expr"].Value.Trim();

                if (type == "var")
                {
                    type = string.Empty;
                }

                if (Utils.Compiler.IsValidIdentifier(variable))
                {
                    return new ParseResult()
                    {
                        Variable = variable,
                        ExpressionString = expr,
                        TypeString = type,
                        IsDeclaring = true,
                        WholeCode = parsedCode
                    };
                }
            }

            return new ParseResult()
            {
                ExpressionString = parsedCode,
                IsDeclaring = false,
                WholeCode = parsedCode
            };
        }

        ///// <summary>
        ///// Figures out which namepaces the expressions uses.
        ///// </summary>
        ///// <param name="parse">Parse of the expression</param>
        ///// <returns></returns>
        ////public static IEnumerable<NameSpaceInfo> FigureOutNamespaces(ParseResult parse)
        ////{

        ////((\w+\.\w+)|(\<[^>]*\>)|new ((\w+\.)*\w+)) Some Ideas...

        ////}

        public static IEnumerable<CodeCompletion> Intellisence(string code)
        {
            var parse = ParseAssigment(code);
            var offset = parse.WholeCode.IndexOf(parse.ExpressionString);
            if (Utils.DotExpressionSearch.IsMatch(parse.ExpressionString))
                return DotExpression(Utils.DotExpressionSearch.Match(parse.ExpressionString), offset);

            Type endType = null;
            while (Utils.DotAfterMethodRegex.IsMatch(code))
            {
                var match = Utils.DotAfterMethodRegex.Match(code);
                var possibleMethods = PossibleMethods(match);
                if (possibleMethods.Count() == 1)
                {
                    var method = possibleMethods.First();
                    endType = method.ReturnType;
                    code = code.Substring(match.Length);
                    offset += match.Length;
                }
                else
                {
                    return Enumerable.Empty<CodeCompletion>();
                }
            }


            //Math.PI.ToString().Trim(',', String.Empty.Length.To)

            var paramatch = Utils.ParameterRegex.Match(parse.ExpressionString);
            if (paramatch.Success)
            {
                IEnumerable<CodeCompletion> methodInfo = MethodsOverload(paramatch, endType);

                var para = paramatch.Groups["para"];
                offset += para.Index;

                var cutIndex = Math.Max(para.Value.LastIndexOf(','), para.Value.LastIndexOf('('));
                var paraVal = para.Value;
                if (cutIndex > 0)
                {
                    offset += cutIndex + 1;
                    paraVal = paraVal.Substring(cutIndex + 1);
                }

                var prevLength = paraVal.Length;
                paraVal = paraVal.TrimStart();
                offset += prevLength - paraVal.Length;

                var match = Utils.DotExpressionSearch.Match(paraVal);
                if (match.Success)
                    return methodInfo.Concat(DotExpression(match, offset));
                else
                    return methodInfo;
            }

            if (endType != null)
            {
                var methodSearch = Utils.DotExpressionSearch.Match(code);
                if (methodSearch.Success)
                {
                    var full = methodSearch.Groups["fullType"];
                    var search = methodSearch.Groups["search"];
                    if (string.IsNullOrEmpty(full.Value))
                    {
                        return ExtractMemberInfo(search, endType, Utils.InstanceBindings, offset);
                    }
                    else
                    {
                        endType = GetLastIndexType(full, endType, 0);
                        if (endType != null)
                            return ExtractMemberInfo(search, endType, Utils.InstanceBindings, offset);
                    }
                }
            }
            return Enumerable.Empty<CodeCompletion>();
        }

        private static IEnumerable<CodeCompletion> MethodsOverload(Match paramatch, Type endType)
        {
            var methodInfo = Enumerable.Empty<CodeCompletion>();
            if (endType == null)
            {
                methodInfo = DotExpression(paramatch, 0);
            }
            else
            {
                var type = GetLastIndexType(paramatch, endType, 0);
                var methodName = paramatch.Groups["search"];
                if (type != null)
                    methodInfo = ExtractMemberInfo(methodName, type, Utils.InstanceBindings, 0);
            }
            foreach (var info in methodInfo)
            {
                info.IsMethodOverload = true;
                yield return info;
            }
        }

        public static IEnumerable<MethodInfo> PossibleMethods(Match match)
        {
            var fullType = match.Groups["fullType"];

            string name;
            Type type;
            if (!TypeOfFirst(fullType, out type, out name))
            {
                return Enumerable.Empty<MethodInfo>();
            }

            GetLastIndexType(fullType, type);

            if (type == null)
            {
                return Enumerable.Empty<MethodInfo>();
            }

            var methodParams = match.Groups["params"].Value;
            var paraCount = 0;
            if (!string.IsNullOrEmpty(methodParams.Trim()))
            {
                while (methodParams.Contains("(") && methodParams.Contains(")"))
                {
                    var index = methodParams.LastIndexOf('(');
                    var length = index - methodParams.Substring(index).IndexOf(')');
                    methodParams = methodParams.Substring(index, length);
                }
                paraCount = methodParams.Count(i => i == ',') + 1;
            }

            var methodName = match.Groups["method"].Value;

            //UTILS ExTENTIONS METHODS
            //var extensionMethods = Utils.GetExtensionMethods(type);
            var possibleMethods = from meth in type.GetMethods()/*.Concat(extensionMethods)*/
                                  where meth.Name == methodName &&
                                  meth.GetParameters().Length == paraCount
                                  select meth;
            return possibleMethods;
        }



        private static IEnumerable<CodeCompletion> DotExpression(Match match, int offset)
        {
            var full = match.Groups["fullType"];
            var first = match.Groups["firstType"];
            var search = match.Groups["search"];

            //Only search..
            if (string.IsNullOrEmpty(first.Value) &&
                !string.IsNullOrEmpty(search.Value) &&
                search.Length > 2)
            {
                return SearchWithoutType(search, offset);
            }

            string name;
            Type type;
            TypeOfFirst(full, out type, out name);

            type = GetLastIndexType(full, type);
            if (type == null)
            {
                return Enumerable.Empty<CodeCompletion>();
            }
            //static info
            if (full.Length == first.Length && !Variables.ContainsKey(name))
            {
                return ExtractMemberInfo(search, type, Utils.StaticBindings, offset);
            }
            else // instance information
            {
                return ExtractMemberInfo(search, type, Utils.InstanceBindings, offset);
            }
        }

        private static bool TypeOfFirst(Group full, out Type theType, out string name)
        {
            name = full.Value.Split('.').First();
            var theName = name;
            //Map to primative if needed
            if (Utils.MapToKeyWords.Values.Contains(name))
            {
                name = Utils.MapToKeyWords.First(i => i.Value == theName).Key.Name;
            }

            if (Variables.ContainsKey(name))
            {
                if (Variables[name].VarValue != null)
                {
                    theType = Variables[name].VarType;
                    return true;
                }
                else
                {
                    theType = null;
                    return false;
                }
            }
            theName = name;
            theType = (from t in Utils.AllVisibleTypes
                       where t.Name == theName
                       select t).FirstOrDefault();
            return theType != null;
        }

        private static IEnumerable<CodeCompletion> SearchWithoutType(Group search, int offset)
        {
            var lowerSearch = search.Value.ToLower();
            var variables = from i in Variables
                            let lowerItem = i.Key.ToLower()
                            where lowerItem.Contains(lowerSearch)
                            select new
                            {
                                SearchName = lowerItem,
                                IsNested = false,
                                ReplacementString = i.Key,
                                Details = new MemberDetails(i.Value.VarValue == null ? new[] {
                                    Syntax.Name(i.Key),
                                    Syntax.EqualsOp,
                                    Syntax.ConstVal("null")
                                } :
                                Utils.GetCSharpRepresentation(i.Value.VarType, false)
                                .Concat(new[] {
                                    Syntax.Name(i.Key),
                                    Syntax.EqualsOp,
                                    Syntax.ConstVal(i.Value.VarValue.ToString())
                                }))
                            };

            var types = from t in Utils.AllVisibleTypes
                        let lowerItem = t.Name.ToLower()
                        where lowerItem.Contains(lowerSearch)
                        select new
                        {
                            SearchName = lowerItem,
                            t.IsNested,
                            ReplacementString = GetNestedName(t),
                            Details = Utils.GetCSharpRepresentation(t, false)
                        };

            return from i in types.Concat(variables)
                   orderby i.SearchName.IndexOf(lowerSearch), i.IsNested, i.SearchName
                   select new CodeCompletion
                   {
                       Details = i.Details,
                       Start = offset,
                       End = offset + search.Length - 1,
                       ReplaceString = i.ReplacementString,//.Details.Name.String,
                       Search = search.Value
                   };
        }


        public static void RemoveOutput(AConsoleOutput deleted)
        {
            outputList.Remove(deleted);
        }

        public static void AddOutput(AConsoleOutput output)
        {
            if (output == null)
                return;
            foreach (var item in outputList)
            {
                item.ShowDetails = false;
                item.ShowMembers = false;
            }
            output.ShowDetails = true;
            output.ShowMembers = true;
            if (outputList.Count >= OUTPUT_LENGHT)
            {
                outputList.RemoveLast();
            }
            outputList.AddFirst(output);
        }
        public static void ClearOutput()
        {
            outputList.Clear();
        }

        public static string GetNestedName(Type type)
        {
            var name = type.Name;
            if (name.IndexOf("`") > -1)
                name = name.Substring(0, name.IndexOf("`"));

            if (type.IsNested)
            {
                return GetNestedName(type.DeclaringType) + "." + name;
            }
            else
            {
                return name;
            }
        }

        /// <summary>
        /// Finds the last index of a qurey.
        /// <para>Example: x.MyProp.AnotherProp   This will navigate down to the AnotherProp</para> 
        /// </summary>
        /// <param name="full"></param>
        /// <param name="variable"></param>
        /// <returns></returns>
        private static Type GetLastIndexType(Group full, Type type, int skipCount = 1)
        {
            var fullPath = full.Value.Split('.').Skip(skipCount).ToList();
            while (fullPath.Any() && type != null)
            {
                var curPath = fullPath.First();
                if (string.IsNullOrEmpty(curPath))
                    break;

                bool found = false;
                var bindings = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

                try
                {
                    foreach (var member in type.GetProperties(bindings))
                    {
                        if (member.Name.Equals(curPath))
                        {
                            type = member.PropertyType;
                            fullPath.RemoveAt(0);
                            found = true;
                            break;
                        }
                    }
                    foreach (var member in type.GetFields(bindings))
                    {
                        if (member.Name.Equals(curPath))
                        {
                            type = member.FieldType;
                            fullPath.RemoveAt(0);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        return null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return type;
        }

        private static readonly Regex propsRegex = new Regex("(.et|add|remove)_(?<Name>.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static IEnumerable<CodeCompletion> ExtractMemberInfo(Group search, Type varType, BindingFlags bindings, int offset)
        {
            var helpList = new Dictionary<string, List<MemberDetails>>();

            foreach (var prop in varType.GetProperties(bindings))
            {
                helpList.Add(prop.Name, new List<MemberDetails> { GetMemberDetails(prop) });
            }

            foreach (var field in varType.GetFields(bindings))
            {
                helpList.Add(field.Name, new List<MemberDetails> { GetMemberDetails(field) });
            }

            foreach (var metod in from met in varType.GetMethods(bindings)
                                  where !propsRegex.IsMatch(met.Name) && met.Name.Contains(search.Value)
                                  select met)
            {
                var infoStr = GetMemberDetails(metod);
                if (!helpList.ContainsKey(metod.Name))
                {
                    helpList.Add(metod.Name, new List<MemberDetails> { infoStr });
                }
                else
                    helpList[metod.Name].Add(infoStr);
            }

            //foreach (var metod in from met in Utils.GetExtensionMethods(varType)
            //                      where met.Name.Contains(search.Value)
            //                      select met)
            //{
            //    var infoStr = GetMemberDetails(metod);
            //    if (!helpList.ContainsKey(metod.Name))
            //    {
            //        helpList.Add(metod.Name, new List<MemberDetails> { infoStr });
            //    }
            //    else
            //        helpList[metod.Name].Add(infoStr);
            //}

            var lowerSearch = search.Value.ToLower();
            return (from item in helpList
                    from val in item.Value
                    let lowerItem = item.Key.ToLower()
                    where lowerItem.Contains(lowerSearch)
                    orderby lowerItem.IndexOf(lowerSearch), lowerItem
                    select new CodeCompletion
                    {
                        Details = val,
                        ReplaceString = val.Name.String,
                        Start = offset + search.Index,
                        End = offset + search.Index + search.Length - 1,
                        Search = search.Value
                    });
        }

        /// <summary>
        /// Uses the property info to build an member details.
        /// </summary>
        /// <param name="prop">property to inspect</param>
        internal static MemberDetails GetMemberDetails(PropertyInfo prop)
        {
            var syntax = new List<Syntax>();

            var get = prop.GetGetMethod();
            var set = prop.GetSetMethod();

            if ((get != null && get.IsStatic) ||
                (set != null && set.IsStatic))
                syntax.Add(Syntax.StaticKeyword);

            syntax.AddRange(Utils.GetCSharpRepresentation(prop.PropertyType));
            syntax.Add(Syntax.Name(prop.Name));
            syntax.Add(Syntax.CurlyOpen);

            if (get != null)
                syntax.AddRange(new[] { Syntax.GetKeyword, Syntax.Semicolon });

            if (set != null)
                syntax.AddRange(new[] { Syntax.SetKeyword, Syntax.Semicolon });

            syntax.Add(Syntax.CurlyClose);

            return new MemberDetails(syntax);
        }

        /// <summary>
        /// Uses the field info to build an member details.
        /// </summary>
        /// <param name="field">field to inspect</param>
        internal static MemberDetails GetMemberDetails(FieldInfo field)
        {
            var syntax = new List<Syntax>();
            if (field.IsStatic && !field.IsLiteral)
                syntax.Add(Syntax.StaticKeyword);

            if (field.IsInitOnly)
                syntax.Add(Syntax.ReadonlyKeyword);

            if (field.IsLiteral)
                syntax.Add(Syntax.ConstKeyword);


            syntax.AddRange(Utils.GetCSharpRepresentation(field.FieldType));
            syntax.Add(Syntax.Name(field.Name));

            if (field.IsLiteral)
                syntax.AddRange(new[] { Syntax.EqualsOp, Syntax.ConstVal(field.GetRawConstantValue().ToString()) });

            return new MemberDetails(syntax);
        }
        /// <summary>
        /// Uses the method info to build an member details.
        /// </summary>
        /// <param name="meth">Method to inspect</param>
        private static MemberDetails GetMemberDetails(MethodInfo meth)
        {
            var syntax = new List<Syntax>();

            if (meth.IsStatic)
                syntax.Add(Syntax.StaticKeyword);

            syntax.AddRange(Utils.GetCSharpRepresentation(meth.ReturnType));
            syntax.Add(Syntax.Name(meth.Name));
            if (meth.IsGenericMethod)
            {
                syntax.Add(Syntax.GenericParaOpen);
                syntax.AddRange(Utils.GenericArgumentsToSyntax(meth.GetGenericArguments().ToList(), false));
                syntax.Add(Syntax.GenericParaClose);
            }

            syntax.Add(Syntax.ParaOpen);

            var paras = meth.GetParameters();
            for (int i = 0; i < paras.Length; i++)
            {
                if (paras[i].IsOut)
                    syntax.Add(Syntax.OutKeyword);
                if (!paras[i].IsOut && !paras[i].IsIn && paras[i].ParameterType.IsByRef)
                    syntax.Add(Syntax.RefKeyword);

                syntax.AddRange(Utils.GetCSharpRepresentation(paras[i].ParameterType));
                syntax.Add(Syntax.ParaName(paras[i].Name));
                if (i + 1 != paras.Length)
                    syntax.Add(Syntax.Comma);
            }
            syntax.Add(Syntax.ParaClose);
            return new MemberDetails(syntax);
        }
        #endregion

        /// <summary>
        /// This is run on a seprate thread than Unity. Used to Load into memory stuff that will be used.
        /// </summary>
        public static void SetupHelper()
        {
            Utils.LoadNamespaceInfos(includeIngoredUsings: false);
            var cmp = Compile(ParseAssigment("1+1"), new Dictionary<string, HistoryItem>());
            Execute<DummyOutput>(cmp, showMessages: true);
        }
        private class DummyOutput : AConsoleOutput
        {
            public DummyOutput() : base() { }

            public override void Display()
            {
                throw new NotImplementedException();
            }

            public override void LoadInDetails(object value, string message, IEnumerable<MemberDetails> details)
            { }
        }
    }
}
