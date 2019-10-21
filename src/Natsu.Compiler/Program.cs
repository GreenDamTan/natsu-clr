﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Natsu.Compiler
{
    class Program
    {
        private static readonly string[] _modulePaths = new[]
        {
            @"..\..\..\..\..\out\bin\netcoreapp3.0\Chino.Kernel.dll",
            @"..\..\..\..\..\out\bin\netcoreapp3.0\Chino.Core.dll",
            //@"..\..\..\..\..\out\bin\netcoreapp3.0\Chino.Chip.K210.dll",
            @"..\..\..\..\..\out\bin\netcoreapp3.0\Chino.Chip.Emulator.dll",
            @"..\..\..\..\..\out\bin\netcoreapp3.0\System.Private.CorLib.dll",
            @"..\..\..\..\..\out\bin\netcoreapp3.0\System.Runtime.dll",
            //@"..\..\..\..\..\out\bin\netcoreapp3.0\System.Diagnostics.Debug.dll",
            //@"..\..\..\..\..\out\bin\netcoreapp3.0\System.Runtime.InteropServices.dll",
            @"..\..\..\..\..\out\bin\netcoreapp3.0\System.Threading.dll",
            //Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".nuget\packages\bitfields\0.1.0\lib\netstandard1.0\BitFields.dll")
        };

        static void Main(string[] args)
        {
            foreach (var path in _modulePaths)
            {
                var module = ModuleDefMD.Load(path);
                var generator = new Generator(module);
                generator.Generate();
            }
        }
    }

    class Generator
    {
        private readonly ModuleDefMD _module;
        private readonly Dictionary<TypeDef, TypeDesc> _typeDescs = new Dictionary<TypeDef, TypeDesc>();
        private readonly List<TypeDesc> _sortedTypeDescs = new List<TypeDesc>();
        private readonly CorLibTypes _corLibTypes;
        private TypeDesc _szArrayType;
        private List<string> _userStrings = new List<string>();

        public Generator(ModuleDefMD module)
        {
            _module = module;
            _corLibTypes = new CorLibTypes(module);
        }

        public void Generate()
        {
            foreach (var type in _module.GetTypes())
            {
                var typeDesc = new TypeDesc(type);
                _typeDescs.Add(type, typeDesc);

                if (type.FullName == "System.SZArray`1")
                    _szArrayType = typeDesc;
            }

            SortTypes();

            var outputPath = Path.GetFullPath(@"..\..\..\..\Native\Generated");
            Directory.CreateDirectory(outputPath);

            using (var writer = new StreamWriter(Path.Combine(outputPath, $"{_module.Assembly.Name}.h"), false, Encoding.UTF8))
            {
                writer.WriteLine("// Generated by natsu clr compiler.");
                writer.WriteLine("#pragma once");
                if (_module.Assembly.Name == "System.Private.CorLib")
                {
                    writer.WriteLine("#include <natsu.typedef.h>");
                }
                else
                {
                    foreach (var ass in _module.GetAssemblyRefs())
                        writer.WriteLine($"#include <{ass.Name}.h>");
                }

                writer.WriteLine();

                writer.WriteLine($"namespace {TypeUtils.EscapeModuleName(_module)}");
                writer.WriteLine("{");
                WriteTypeForwards(writer);
                writer.WriteLine();
                WriteTypeForwardDeclares(writer);
                writer.WriteLine();
                WriteTypeDeclares(writer);
                writer.WriteLine("}");
                writer.WriteLine();

                if (_module.Assembly.Name == "System.Private.CorLib")
                {
                    writer.WriteLine("#include <natsu.runtime.h>");
                    writer.WriteLine();
                }
                else if (_module.Assembly.Name == "Chino.Core")
                {
                    writer.WriteLine("#include <chino.runtime.h>");
                    writer.WriteLine();
                }

                using (var writerSrc = new StreamWriter(Path.Combine(outputPath, $"{_module.Assembly.Name}.cpp"), false, Encoding.UTF8))
                {
                    writerSrc.WriteLine("// Generated by natsu clr compiler.");
                    writerSrc.WriteLine($"#include \"{_module.Assembly.Name}.h\"");
                    writerSrc.WriteLine();

                    writerSrc.WriteLine($"namespace {TypeUtils.EscapeModuleName(_module)}");
                    writerSrc.WriteLine("{");
                    WriteTypeMethodsBody(writerSrc, false);
                    WriteConstantStringFields(writerSrc);
                    writerSrc.WriteLine("}");
                }

                var hBody = new StringWriter();

                hBody.WriteLine($"namespace {TypeUtils.EscapeModuleName(_module)}");
                hBody.WriteLine("{");
                WriteTypeMethodsBody(hBody, true);
                hBody.WriteLine("}");

                writer.WriteLine($"namespace {TypeUtils.EscapeModuleName(_module)}");
                writer.WriteLine("{");
                WriteUserStrings(writer);
                writer.WriteLine("}");

                writer.WriteLine();
                writer.WriteLine(hBody.ToString());
            }
        }

        private void WriteUserStrings(StreamWriter writer)
        {
            for (int i = 0; i < _userStrings.Count; i++)
            {
                writer.Ident(1).WriteLine($"static const natsu::static_object<::System_Private_CorLib::System::String, natsu::string_literal<{_userStrings[i].Length}>> user_string_{i}(uR\"NS({_userStrings[i]})NS\");");
            }

            writer.WriteLine();
        }

        private void WriteConstantStringFields(StreamWriter writer)
        {
            foreach (var type in _sortedTypeDescs)
            {
                if (!type.TypeDef.IsEnum)
                {
                    foreach (var field in type.TypeDef.Fields)
                    {
                        if (field.HasConstant && field.ElementType == ElementType.String)
                            WriteConstantStringField(writer, 0, field);
                    }
                }
            }
        }

        private void SortTypes()
        {
            foreach (var type in _typeDescs.Values)
            {
                foreach (var field in type.TypeDef.Fields)
                    AddTypeRef(type, field.FieldType, false);

                var baseType = GetTypeDef(GetBaseType(type.TypeDef));
                if (baseType != null)
                    AddTypeRef(type, baseType, true);

                foreach (var iface in type.TypeDef.Interfaces)
                {
                    var typeDef = GetTypeDef(iface.Interface);
                    AddTypeRef(type, typeDef, true);
                }
            }

            var visited = new HashSet<TypeDesc>();
            void VisitType(TypeDesc type)
            {
                if (visited.Add(type))
                {
                    foreach (var parent in type.UsedTypes)
                        VisitType(parent);
                    _sortedTypeDescs.Add(type);
                }
            }

            foreach (var type in _typeDescs.Values)
                VisitType(type);
        }

        private TypeDef GetTypeDef(ITypeDefOrRef type)
        {
            var typeDef = type as TypeDef;
            if (typeDef == null)
            {
                if (type is TypeSpec typeSpec)
                {
                    var cntSig = typeSpec.TypeSig;
                    while (true)
                    {
                        switch (cntSig.ElementType)
                        {
                            case ElementType.Class:
                                {
                                    var sig = cntSig.ToClassSig();
                                    return sig.TypeDef;
                                }
                            case ElementType.GenericInst:
                                {
                                    var sig = cntSig.ToGenericInstSig();
                                    cntSig = sig.GenericType;
                                    break;
                                }
                            default:
                                cntSig = cntSig.Next;
                                break;
                        }

                        if (typeDef != null)
                            return typeDef;

                        if (cntSig == null)
                            return null;
                    }
                }
            }

            return typeDef;
        }

        private ITypeDefOrRef GetBaseType(TypeDef type)
        {
            return type.BaseType;
        }

        private void AddTypeRef(TypeDesc declareDesc, TypeSig fieldType, bool force)
        {
            var cntSig = fieldType;
            while (cntSig != null)
            {
                switch (cntSig.ElementType)
                {
                    case ElementType.Void:
                    case ElementType.Var:
                    case ElementType.ByRef:
                    case ElementType.Ptr:
                    case ElementType.CModReqd:
                    case ElementType.GenericInst:
                    case ElementType.Object:
                    case ElementType.Class:
                        break;
                    case ElementType.Boolean:
                    case ElementType.Char:
                    case ElementType.I1:
                    case ElementType.U1:
                    case ElementType.I2:
                    case ElementType.U2:
                    case ElementType.I4:
                    case ElementType.U4:
                    case ElementType.I8:
                    case ElementType.U8:
                    case ElementType.R4:
                    case ElementType.R8:
                    case ElementType.String:
                    case ElementType.I:
                    case ElementType.U:
                        {
                            if (cntSig.TryGetTypeDef() != declareDesc.TypeDef)
                                AddTypeRef(declareDesc, cntSig.TryGetTypeDef(), force);
                        }
                        break;
                    case ElementType.ValueType:
                        AddTypeRef(declareDesc, cntSig.TryGetTypeDef(), force);
                        break;
                    case ElementType.SZArray:
                        AddTypeRef(declareDesc, _szArrayType.TypeDef, force);
                        break;
                    default:
                        throw new NotSupportedException();
                }

                cntSig = cntSig.Next;
            }
        }

        private void AddTypeRef(TypeDesc declareDesc, TypeDef typeDef, bool force)
        {
            if (typeDef != null && (force || typeDef.IsValueType))
            {
                if (_typeDescs.TryGetValue(typeDef, out var targetDesc))
                {
                    if (declareDesc != targetDesc)
                    {
                        declareDesc.UsedTypes.Add(targetDesc);
                        targetDesc.UsedByTypes.Add(declareDesc);
                    }
                }
            }
        }

        private void WriteTypeForwards(StreamWriter writer)
        {
            var types = _module.ExportedTypes.Where(x => x.Attributes == TypeAttributes.Forwarder).ToList();
            var index = 0;
            foreach (var type in types)
            {
                WriteTypeForward(writer, 0, type);
                if (index++ != types.Count - 1)
                    writer.WriteLine();
            }

            if (types.Any())
                writer.WriteLine();
        }

        private void WriteTypeForward(StreamWriter writer, int ident, dnlib.DotNet.ExportedType type)
        {
            var nss = type.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(TypeUtils.EscapeNamespaceName).ToList();

            writer.Ident(ident);
            foreach (var ns in nss)
                writer.Write($"namespace {ns} {{ ");

            var fowardName = TypeUtils.EscapeTypeName(type.ToTypeRef());
            if (type.Name.Contains("`"))
            {
                var types = int.Parse(type.Name.Substring(type.Name.IndexOf('`') + 1));
                var genDecl = $"<{string.Join(", ", Enumerable.Range(0, types).Select(x => "class T" + x))}>";
                var genImpl = $"<{string.Join(", ", Enumerable.Range(0, types).Select(x => "T" + x))}>";
                writer.Ident(ident).Write($"template {genDecl} ");
                writer.Ident(ident).Write($"using {TypeUtils.EscapeTypeName(type.FullName)} = {fowardName}{genImpl};");
            }
            else
            {
                writer.Ident(ident).Write($"using {TypeUtils.EscapeTypeName(type.FullName)} = {fowardName}; ");
            }

            foreach (var ns in nss)
                writer.Write(" }");
        }

        #region Forward Declares
        private void WriteTypeForwardDeclares(StreamWriter writer)
        {
            var types = _typeDescs.Values.ToList();
            var index = 0;
            foreach (var type in types)
            {
                WriteTypeForwardDeclare(writer, 0, type);
                if (index++ != types.Count - 1)
                    writer.WriteLine();
            }
        }

        private void WriteTypeForwardDeclare(StreamWriter writer, int ident, TypeDesc type)
        {
            var nss = type.TypeDef.Namespace.String.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(TypeUtils.EscapeNamespaceName).ToList();

            writer.Ident(ident);
            foreach (var ns in nss)
                writer.Write($"namespace {ns} {{ ");

            if (type.TypeDef.HasGenericParameters)
            {
                var typeNames = type.TypeDef.GenericParameters.Select(x => "class " + x.Name.String).ToList();
                writer.Ident(ident).Write($"template <{string.Join(", ", typeNames)}> ");
            }

            writer.Ident(ident).Write($"struct {type.Name};");

            foreach (var ns in nss)
                writer.Write(" }");
        }
        #endregion

        #region Declares

        private void WriteTypeDeclares(StreamWriter writer)
        {
            var index = 0;
            foreach (var type in _sortedTypeDescs)
            {
                WriteTypeDeclare(writer, 0, type);
                if (index++ != _sortedTypeDescs.Count - 1)
                    writer.WriteLine();
            }
        }

        private void WriteTypeDeclare(StreamWriter writer, int ident, TypeDesc type)
        {
            bool hasStaticMember = false;

            var nss = type.TypeDef.Namespace.String.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(TypeUtils.EscapeNamespaceName).ToList();

            writer.Ident(ident);
            foreach (var ns in nss)
                writer.Write($"namespace {ns} {{ ");

            writer.WriteLine();
            if (type.TypeDef.HasGenericParameters)
            {
                var typeNames = type.TypeDef.GenericParameters.Select(x => "class " + x.Name.String).ToList();
                writer.Ident(ident).WriteLine($"template <{string.Join(", ", typeNames)}> ");
            }

            writer.Ident(ident).Write($"struct {type.Name}");
            if (!type.TypeDef.IsValueType)
            {
                var baseType = GetBaseType(type.TypeDef);
                if (baseType != null)
                    writer.WriteLine(" : public " + TypeUtils.EscapeTypeName(baseType));
                else
                    writer.WriteLine();
            }
            else
            {
                writer.WriteLine();
            }

            writer.Ident(ident).WriteLine("{");

            // TypeInfo
            WriteTypeInfo(writer, ident + 1, type);
            writer.WriteLine();
            // VTable
            WriteVTableDeclare(writer, ident + 1, type);
            writer.WriteLine();

            var hasSize = type.TypeDef.HasClassLayout && type.TypeDef.ClassLayout.ClassSize != 0;
            string fieldSize = "0";
            foreach (var field in type.TypeDef.Fields)
            {
                if (field.HasConstant && field.ElementType != ElementType.String)
                    WriteConstantField(writer, ident + 1, field);
                else if (!field.IsStatic || field.ElementType == ElementType.String)
                    WriteField(writer, ident + 1, field);
                else
                    hasStaticMember = true;

                if (hasSize && !field.IsStatic)
                    fieldSize += " + " + TypeUtils.GetTypeSize(field.FieldType.ElementType);
            }

            if (hasSize)
                writer.Ident(ident + 1).WriteLine($"uint8_t padding_[{type.TypeDef.ClassLayout.ClassSize} - ({fieldSize})];");

            writer.WriteLine();

            foreach (var method in type.TypeDef.Methods)
            {
                if (method.IsStaticConstructor)
                {
                    hasStaticMember = true;
                }
                else if (!method.IsAbstract)
                {
                    WriteMethodDeclare(writer, ident + 1, method);
                }
            }

            if (type.TypeDef.IsPrimitive)
            {
                writer.WriteLine();
                writer.Ident(ident + 1).WriteLine($"NATSU_PRIMITIVE_IMPL_{type.TypeDef.Name.ToUpperInvariant()}");
            }

            if (type.TypeDef.IsEnum)
            {
                writer.WriteLine();
                writer.Ident(ident + 1).WriteLine($"NATSU_ENUM_IMPL_{type.TypeDef.GetEnumUnderlyingType().TypeName.ToUpperInvariant()}({type.Name})");
            }

            if (type.TypeDef.ToTypeSig().ElementType == ElementType.Object)
            {
                writer.WriteLine();
                writer.Ident(ident + 1).WriteLine($"NATSU_OBJECT_IMPL");
            }

            if (type.TypeDef == _szArrayType?.TypeDef)
            {
                writer.WriteLine();
                writer.Ident(ident + 1).WriteLine($"NATSU_SZARRAY_IMPL");
            }

            // Static
            if (hasStaticMember)
            {
                writer.WriteLine();
                writer.Ident(ident + 1).WriteLine("struct Static;");
            }

            writer.Ident(ident).WriteLine("};");

            // Static
            if (hasStaticMember)
            {
                writer.WriteLine();
                WriteStatic(writer, ident, type);
            }

            foreach (var ns in nss)
                writer.Write("} ");
            writer.WriteLine();
        }

        private void WriteTypeInfo(StreamWriter writer, int ident, TypeDesc type)
        {
            writer.Ident(ident).WriteLine($"struct TypeInfo");
            writer.Ident(ident).WriteLine("{");

            // IsValueType
            writer.Ident(ident + 1).WriteLine($"static constexpr bool IsValueType = {type.TypeDef.IsValueType.ToString().ToLower()};");
            // IsEnum
            writer.Ident(ident + 1).WriteLine($"static constexpr bool IsEnum = {type.TypeDef.IsEnum.ToString().ToLower()};");

            writer.Ident(ident).WriteLine("};");
        }

        private void WriteVTableDeclare(StreamWriter writer, int ident, TypeDesc type)
        {
            writer.Ident(ident).Write($"struct VTable");
            if (!type.TypeDef.IsInterface)
            {
                writer.Write(" : public ::natsu::vtable_class<");
                var baseType = GetBaseType(type.TypeDef);
                if (baseType == null)
                    writer.Write("natsu::vtable_t");
                else
                    writer.Write($"typename {TypeUtils.EscapeTypeName(baseType)}::VTable");

                foreach (var iface in type.TypeDef.Interfaces)
                {
                    var ifaceType = iface.Interface;
                    writer.Write($", typename {TypeUtils.EscapeTypeName(ifaceType)}::VTable");
                }

                writer.WriteLine(">");
            }
            else
            {
                writer.WriteLine();
            }

            writer.Ident(ident).WriteLine("{");

            foreach (var method in type.TypeDef.Methods)
            {
                if (!method.IsInstanceConstructor && !method.IsStatic)
                    WriteVTableMethodDeclare(writer, ident + 1, method);
            }

            writer.Ident(ident).WriteLine("};");
        }

        private void WriteStatic(StreamWriter writer, int ident, TypeDesc type)
        {
            if (type.TypeDef.HasGenericParameters)
            {
                var typeNames = type.TypeDef.GenericParameters.Select(x => "class " + x.Name.String).ToList();
                writer.Ident(ident).WriteLine($"template <{string.Join(", ", typeNames)}> ");
            }

            writer.Ident(ident).Write($"struct {type.Name}");
            if (type.TypeDef.HasGenericParameters)
            {
                var typeNames = type.TypeDef.GenericParameters.Select(x => x.Name.String).ToList();
                writer.Ident(ident).WriteLine($"<{string.Join(", ", typeNames)}> ");
            }

            writer.WriteLine("::Static");
            writer.Ident(ident).WriteLine("{");

            foreach (var field in type.TypeDef.Fields)
            {
                if (field.IsStatic && !field.HasConstant)
                    WriteField(writer, ident + 1, field, true);
            }

            writer.WriteLine();

            foreach (var method in type.TypeDef.Methods)
            {
                if (method.IsStaticConstructor)
                    WriteMethodDeclare(writer, ident + 1, method);
            }

            writer.Ident(ident).WriteLine("};");
        }

        private void WriteField(StreamWriter writer, int ident, FieldDef value, bool isStatic = false)
        {
            string prefix = string.Empty;
            if (value.IsStatic && !isStatic)
                prefix = "static ";

            writer.Ident(ident).WriteLine($"{prefix}{TypeUtils.EscapeVariableTypeName(value.FieldType, value.DeclaringType)} {TypeUtils.EscapeIdentifier(value.Name)};");
        }

        private void WriteConstantStringField(StreamWriter writer, int ident, FieldDef value)
        {
            writer.Ident(ident).WriteLine($"{TypeUtils.EscapeVariableTypeName(value.FieldType, value.DeclaringType)} {TypeUtils.EscapeTypeName(value.DeclaringType, hasModuleName: false)}::{TypeUtils.EscapeIdentifier(value.Name)} = ::natsu::load_string(uR\"NS({value.Constant.Value})NS\"sv);");
        }

        private void WriteMethodDeclare(TextWriter writer, int ident, MethodDef method)
        {
            var methodGens = new List<string>();

            if (method.HasGenericParameters)
                methodGens.AddRange(method.GenericParameters.Select(x => x.Name.String));

            if (methodGens.Any())
                writer.Ident(ident).WriteLine($"template <{string.Join(", ", methodGens.Select(x => "class " + x))}>");
            if (!method.IsStaticConstructor)
                writer.Ident(ident).Write("static " + TypeUtils.EscapeVariableTypeName(method.ReturnType) + " ");
            else
                writer.Ident(ident);
            writer.Write(TypeUtils.EscapeMethodName(method) + "(");
            WriteParameterList(writer, method.Parameters);
            writer.WriteLine($");");
        }

        private void WriteParameterList(TextWriter writer, ParameterList parameters, bool hasType = true, bool isVTable = false)
        {
            var index = 0;
            var method = parameters.Method;
            foreach (var param in parameters)
            {
                if (hasType)
                {
                    if (isVTable && method.IsVirtual && param.IsHiddenThisParameter)
                        writer.Write("::natsu::gc_obj_ref<::System_Private_CorLib::System::Object>");
                    else
                        writer.Write(TypeUtils.EscapeVariableTypeName(param.Type, hasGen: 1) + " ");
                }

                var paramName = param.IsHiddenThisParameter ? "_this" : param.ToString();
                if (!hasType && isVTable && method.IsVirtual && param.IsHiddenThisParameter)
                {
                    if (method.DeclaringType.IsValueType)
                    {
                        writer.Write($"::natsu::unbox_exact<{TypeUtils.EscapeTypeName(method.DeclaringType)}>({TypeUtils.EscapeIdentifier(paramName)})");
                    }
                    else
                    {
                        writer.Write($"{TypeUtils.EscapeIdentifier(paramName)}.cast<{TypeUtils.EscapeTypeName(method.DeclaringType)}>()");
                    }
                }
                else
                {
                    writer.Write(TypeUtils.EscapeIdentifier(paramName));
                }

                if (index++ != parameters.Count - 1)
                    writer.Write(", ");
            }
        }

        private void WriteConstantField(StreamWriter writer, int ident, FieldDef value)
        {
            string prefix = string.Empty;
            if (value.IsStatic)
                prefix = "static ";

            writer.Ident(ident).WriteLine($"{prefix}constexpr {TypeUtils.GetConstantTypeName(value.ElementType)} {TypeUtils.EscapeIdentifier(value.Name)} = {TypeUtils.LiteralConstant(value.Constant.Value)};");
        }
        #endregion

        private void WriteTypeMethodsBody(TextWriter writer, bool inHeader)
        {
            foreach (var type in _sortedTypeDescs)
            {
                WriteTypeMethodBody(writer, 0, type, inHeader);
            }
        }

        private void WriteTypeMethodBody(TextWriter writer, int ident, TypeDesc type, bool inHeader)
        {
            foreach (var method in type.TypeDef.Methods)
            {
                if (inHeader == (type.TypeDef.HasGenericParameters || method.HasGenericParameters))
                {
                    if (!method.IsInstanceConstructor && !method.IsStatic)
                    {
                        WriteVTableMethodBody(writer, ident, method);
                        writer.WriteLine();
                    }

                    if (!method.IsAbstract && !method.IsInternalCall
                        && (method.HasBody || method.IsRuntime))
                    {
                        WriteMethodBody(writer, ident, method);
                        writer.WriteLine();
                    }
                }
            }
        }

        private void WriteMethodBody(TextWriter writer, int ident, MethodDef method)
        {
            writer.Ident(ident);
            var typeGens = new List<string>();
            var methodGens = new List<string>();

            if (method.DeclaringType.HasGenericParameters)
                typeGens.AddRange(method.DeclaringType.GenericParameters.Select(x => x.Name.String));
            if (method.HasGenericParameters)
                methodGens.AddRange(method.GenericParameters.Select(x => x.Name.String));

            if (typeGens.Any() || methodGens.Any())
                writer.WriteLine($"template <{string.Join(", ", typeGens.Concat(methodGens).Select(x => "class " + x))}>");

            if (!method.IsStaticConstructor)
                writer.Write(TypeUtils.EscapeVariableTypeName(method.ReturnType) + " ");
            writer.Write(TypeUtils.EscapeTypeName(method.DeclaringType, hasModuleName: false));
            if (method.IsStaticConstructor)
                writer.Write("::Static");
            writer.Write("::" + TypeUtils.EscapeMethodName(method) + "(");
            WriteParameterList(writer, method.Parameters);
            writer.WriteLine(")");
            writer.Ident(ident).WriteLine("{");
            if (method.HasBody)
                WriteILBody(writer, ident + 1, method);
            else if (method.IsRuntime)
                WriteRuntimeBody(writer, ident + 1, method);
            else
                throw new NotSupportedException();
            writer.Ident(ident).WriteLine("}");
            writer.Flush();
        }

        private void WriteVTableMethodDeclare(TextWriter writer, int ident, MethodDef method)
        {
            writer.Ident(ident);

            var methodGens = new List<string>();

            if (method.HasGenericParameters)
                methodGens.AddRange(method.GenericParameters.Select(x => x.Name.String));

            if (methodGens.Any())
                writer.Ident(ident).WriteLine($"template <{string.Join(", ", methodGens.Select(x => "class " + x))}>");

            if (method.IsVirtual)
            {
                if (method.HasGenericParameters)
                    throw new NotSupportedException("Virtual generic methods is not supported");

                if (method.IsNewSlot)
                    writer.Write("virtual ");
            }

            writer.Write(TypeUtils.EscapeVariableTypeName(method.ReturnType) + " ");
            writer.Write(TypeUtils.EscapeMethodName(method) + "(");
            WriteParameterList(writer, method.Parameters, isVTable: true);
            writer.Write(") const");
            if (method.IsAbstract)
            {
                writer.WriteLine(";");
            }
            else
            {
                if (method.IsVirtual)
                {
                    if (!method.IsNewSlot)
                        writer.Write(" override");
                }

                writer.WriteLine(";");
            }

            writer.Flush();
        }

        private void WriteVTableMethodBody(TextWriter writer, int ident, MethodDef method)
        {
            writer.Ident(ident);
            var typeGens = new List<string>();
            var methodGens = new List<string>();

            if (method.DeclaringType.HasGenericParameters)
                typeGens.AddRange(method.DeclaringType.GenericParameters.Select(x => x.Name.String));
            if (method.IsVirtual && method.HasGenericParameters)
                throw new NotSupportedException("Virtual generic methods is not supported");
            if (method.HasGenericParameters)
                methodGens.AddRange(method.GenericParameters.Select(x => x.Name.String));

            if (typeGens.Any() || methodGens.Any())
                writer.WriteLine($"template <{string.Join(", ", typeGens.Concat(methodGens).Select(x => "class " + x))}>");

            writer.Write(TypeUtils.EscapeVariableTypeName(method.ReturnType) + " ");
            writer.Write(TypeUtils.EscapeTypeName(method.DeclaringType, hasModuleName: false));
            writer.Write("::VTable::" + TypeUtils.EscapeMethodName(method) + "(");
            WriteParameterList(writer, method.Parameters, isVTable: true);
            writer.WriteLine(") const");
            writer.Ident(ident).WriteLine("{");
            if (method.IsAbstract)
            {
                writer.Ident(ident + 1).WriteLine("::natsu::pure_call();");
            }
            else
            {
                writer.Ident(ident + 1).Write("return ");
                writer.Write(TypeUtils.EscapeTypeName(method.DeclaringType));
                writer.Write("::" + TypeUtils.EscapeMethodName(method) + "(");
                WriteParameterList(writer, method.Parameters, hasType: false, isVTable: true);
                writer.WriteLine(");");
            }
            writer.Ident(ident).WriteLine("}");

            writer.Flush();
        }

        private void WriteILBody(TextWriter writer, int ident, MethodDef method)
        {
            var body = method.Body;

            foreach (var local in body.Variables)
            {
                WriteLocal(local, writer, ident, method);
            }

            var importer = new ILImporter(method, writer, ident) { UserStrings = _userStrings, ModuleName = TypeUtils.EscapeModuleName(_module.Assembly) };
            importer.ImportBlocks(body.Instructions);
            importer.Gencode();
        }

        private void WriteRuntimeBody(TextWriter writer, int ident, MethodDef method)
        {
            if (method.DeclaringType.IsDelegate)
            {
                if (method.IsInstanceConstructor)
                {
                    writer.Ident(ident).WriteLine($"_this->_target = object;");
                    writer.Ident(ident).WriteLine($"_this->_methodPtr = method;");
                }
                else if (method.Name == "Invoke")
                {
                    writer.Ident(ident).Write($"typedef {TypeUtils.EscapeVariableTypeName(method.ReturnType)}(*method_t)(::natsu::gc_obj_ref<::System_Private_CorLib::System::Object>");
                    foreach (var param in method.Parameters.Skip(1))
                        writer.Write(", " + TypeUtils.EscapeVariableTypeName(param.Type, hasGen: 1));
                    writer.WriteLine(");");

                    writer.Ident(ident).WriteLine($"if (!_this->_invocationList)");
                    writer.Ident(ident).WriteLine("{");
                    writer.Ident(ident + 1).Write($"return reinterpret_cast<method_t>((intptr_t)_this->_methodPtr)(_this->_target");
                    foreach (var param in method.Parameters.Skip(1))
                    {
                        var paramName = param.IsHiddenThisParameter ? "_this" : param.ToString();
                        writer.Write(", " + paramName);
                    }
                    writer.WriteLine($");");
                    writer.Ident(ident).WriteLine("}");

                    writer.Ident(ident).WriteLine($"else");
                    writer.Ident(ident).WriteLine("{");
                    if (method.HasReturnType)
                        writer.Ident(ident + 1).WriteLine($"{TypeUtils.EscapeVariableTypeName(method.ReturnType)} result;");
                    writer.Ident(ident + 1).WriteLine("for (auto d : *_this->_invocationList)");
                    writer.Ident(ident + 1).WriteLine("{");
                    writer.Ident(ident + 2).WriteLine($"auto typed_d = d.cast<{TypeUtils.EscapeTypeName(method.DeclaringType)}>();");
                    if (method.HasReturnType)
                        writer.Ident(ident + 2).Write($"result = reinterpret_cast<method_t>((intptr_t)typed_d->_methodPtr)(typed_d->_target");
                    else
                        writer.Ident(ident + 2).Write($"reinterpret_cast<method_t>((intptr_t)typed_d->_methodPtr)(typed_d->_target");
                    foreach (var param in method.Parameters.Skip(1))
                    {
                        var paramName = param.IsHiddenThisParameter ? "_this" : param.ToString();
                        writer.Write(", " + paramName);
                    }
                    writer.WriteLine($");");
                    writer.Ident(ident + 1).WriteLine("}");
                    if (method.HasReturnType)
                        writer.Ident(ident + 1).WriteLine($"return result;");
                    writer.Ident(ident).WriteLine("}");
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        private void WriteLocal(Local local, TextWriter writer, int ident, MethodDef method)
        {
            writer.Ident(ident).WriteLine($"{TypeUtils.EscapeVariableTypeName(local.Type)} _l{local.Index};");
        }

        class TypeDesc
        {
            public TypeDef TypeDef { get; }

            public UTF8String Name { get; set; }

            public string QualifiedName { get; set; }

            public Dictionary<UTF8String, TypeDesc> Nested { get; } = new Dictionary<UTF8String, TypeDesc>();

            public HashSet<TypeDesc> UsedTypes { get; } = new HashSet<TypeDesc>();

            public HashSet<TypeDesc> UsedByTypes { get; } = new HashSet<TypeDesc>();

            public TypeDesc(TypeDef typeDef)
            {
                TypeDef = typeDef;
                Name = TypeUtils.EscapeTypeName(typeDef.FullName);
                QualifiedName = Name;
            }

            public override string ToString()
            {
                return QualifiedName;
            }
        }
    }

    internal static class Extensions
    {
        public static TextWriter Ident(this TextWriter writer, int ident)
        {
            for (int i = 0; i < ident; i++)
                writer.Write("    ");
            return writer;
        }
    }
}
