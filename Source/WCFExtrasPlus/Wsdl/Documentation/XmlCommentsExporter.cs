using System;
using System.Collections.Generic;
using System.ServiceModel.Description;
using System.Web.Services.Description;
using System.Xml;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Schema;
using WCFExtrasPlus.Utils;
using System.Linq;

namespace WCFExtrasPlus.Wsdl.Documentation
{
    class XmlCommentsExporter
    {
        private static void InitXsdDataContractExporter(WsdlExporter exporter, XmlCommentFormat format)
        {
            object dataContractExporter;
            XsdDataContractExporter xsdExporter;
            if (!exporter.State.TryGetValue(typeof(XsdDataContractExporter),
                out dataContractExporter))
            {
                xsdExporter = new XsdDataContractExporter(exporter.GeneratedXmlSchemas);
                exporter.State.Add(typeof(XsdDataContractExporter), xsdExporter);
            }
            else
            {
                xsdExporter = (XsdDataContractExporter)dataContractExporter;
            }

            if (xsdExporter.Options == null)
                xsdExporter.Options = new ExportOptions();
            if (!(xsdExporter.Options.DataContractSurrogate is XmlCommentsDataSurrogate))
                xsdExporter.Options.DataContractSurrogate = new XmlCommentsDataSurrogate(xsdExporter.Options.DataContractSurrogate, format);
        }

        private static void ConvertObjectAnnotation(XmlSchemaObject schemaObj)
        {
            XmlSchemaAnnotated annObj = schemaObj as XmlSchemaAnnotated;

            if (annObj != null && annObj.Annotation != null)
            {
                XmlSchemaDocumentation comment = null;
                foreach (XmlSchemaObject annotation in annObj.Annotation.Items)
                {
                    XmlSchemaAppInfo appInfo = annotation as XmlSchemaAppInfo;
                    if (appInfo != null)
                    {
                        for (int i = 0; i < appInfo.Markup.Length; i++)
                        {
                            XmlNode markup = appInfo.Markup[i];
                            if (markup != null)
                            {
                                XmlAttribute typeAttrib = markup.Attributes["type", "http://www.w3.org/2001/XMLSchema-instance"];
                                if (typeAttrib != null)
                                {
                                    if (typeAttrib.Value.Contains(":Annotation"))
                                    {
                                        string ns = typeAttrib.Value.Split(':')[0];
                                        typeAttrib = markup.Attributes[ns, "http://www.w3.org/2000/xmlns/"];
                                        if (typeAttrib != null && typeAttrib.Value == Annotation.AnnotationNamespace)
                                        {
                                            //This is an annotation created by us. Convert it to ws:documentation element and break
                                            comment = CreateDocumentationItem(markup.InnerText);
                                            appInfo.Markup[i] = null;
                                            break;
                                        }
                                    }
                                    else if (typeAttrib.Value.Contains(":EnumAnnotation"))
                                    {
                                        string ns = typeAttrib.Value.Split(':')[0];
                                        typeAttrib = markup.Attributes[ns, "http://www.w3.org/2000/xmlns/"];
                                        if (typeAttrib != null && typeAttrib.Value == Annotation.AnnotationNamespace)
                                        {
                                            DataContractSerializer serializer = new DataContractSerializer(typeof(EnumAnnotation));
                                            using (XmlReader reader = new XmlNodeReader(markup))
                                            {
                                                EnumAnnotation enumAnn = (EnumAnnotation)serializer.ReadObject(reader, false);
                                                if (enumAnn.EnumText != null)
                                                {
                                                    //This is an annotation created by us. Convert it to ws:documentation element and break
                                                    comment = CreateDocumentationItem(enumAnn.EnumText);
                                                }
                                                if (enumAnn.Members.Count > 0)
                                                {
                                                    foreach (XmlSchemaEnumerationFacet enumObj in GetEnumItems(schemaObj))
                                                    {
                                                        string docText;
                                                        if (enumAnn.Members.TryGetValue(enumObj.Value, out docText))
                                                        {
                                                            if (enumObj.Annotation == null)
                                                                enumObj.Annotation = new XmlSchemaAnnotation();
                                                            enumObj.Annotation.Items.Add(CreateDocumentationItem(docText));
                                                        }
                                                    }
                                                }
                                                appInfo.Markup[i] = null;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                if (comment != null)
                    annObj.Annotation.Items.Add(comment);
            }

            foreach (XmlSchemaObject subObj in GetSubItems(schemaObj))
            {
                ConvertObjectAnnotation(subObj);
            }
        }

        private static XmlSchemaDocumentation CreateDocumentationItem(string text)
        {
            XmlDocument doc = new XmlDocument();
            XmlSchemaDocumentation comment = new XmlSchemaDocumentation();
            XmlNode node = doc.CreateTextNode(text);
            comment.Markup = new XmlNode[] { node };
            return comment;
        }

        private static IEnumerable<XmlSchemaObject> GetEnumItems(XmlSchemaObject schemaObj)
        {
            XmlSchemaSimpleType simpleType = (schemaObj as XmlSchemaSimpleType);
            if (simpleType != null)
            {
                XmlSchemaSimpleTypeRestriction restriction = (simpleType.Content as XmlSchemaSimpleTypeRestriction);
                if (restriction != null)
                {
                    foreach (XmlSchemaObject obj in restriction.Facets)
                        yield return obj;
                }
            }

            XmlSchemaSimpleTypeList flagEnumTypeList = (simpleType.Content as XmlSchemaSimpleTypeList);
            if (flagEnumTypeList != null && flagEnumTypeList.ItemType != null)
            {
                XmlSchemaSimpleTypeRestriction flagEnumRestriction = (flagEnumTypeList.ItemType.Content as XmlSchemaSimpleTypeRestriction);
                if (flagEnumRestriction != null)
                {
                    foreach (XmlSchemaObject obj in flagEnumRestriction.Facets)
                        yield return obj;
                }
            }
        }

        private static IEnumerable<XmlSchemaObject> GetSubItems(XmlSchemaObject schemaObj)
        {
            XmlSchemaComplexType complexType = schemaObj as XmlSchemaComplexType;
            if (complexType != null)
            {
                XmlSchemaSequence seq = complexType.ContentTypeParticle as XmlSchemaSequence;
                if (seq != null)
                {
                    foreach (XmlSchemaObject subObj in seq.Items)
                    {
                        yield return subObj;
                    }
                }
            }
        }

        internal static void ExportEndpoint(WsdlExporter exporter, XmlCommentFormat format)
        {
            foreach (XmlSchema schema in exporter.GeneratedXmlSchemas.Schemas())
            {
                foreach (XmlSchemaObject schemaObj in schema.Items)
                {
                    ConvertObjectAnnotation(schemaObj);
                }
            }

            GenerateDocumentation_ForParameters(exporter, true);

            XmlCommentsUtils.ClearCache();
        }

        private static void GenerateDocumentation_ForParameters(WsdlExporter exporter, bool modifyOperationDocumentation)
        {
            foreach (var document in exporter.GeneratedWsdlDocuments)
            {
                var service = document as System.Web.Services.Description.ServiceDescription;
                if (service != null)
                {
                    foreach (var portTypeObject in service.PortTypes)
                    {
                        var portType = (PortType)portTypeObject;
                        foreach (var operationObject in portType.Operations)
                        {
                            var operation = (Operation)operationObject;

                            if (operation.Documentation != null)
                            {
                                var parameterDocumentationMap = new Dictionary<string, string>();

                                XmlDocument xmlDoc = new XmlDocument();
                                xmlDoc.LoadXml("<root>" + operation.Documentation + "</root>");

                                foreach (XmlNode paramNode in xmlDoc.SelectNodes("root/param"))
                                {
                                    var paramName = paramNode.Attributes["name"].Value;
                                    var paramDescription = paramNode.InnerText;
                                    parameterDocumentationMap.Add(paramName, paramDescription);
                                }

                                var returnNodes = xmlDoc.SelectNodes("root/returns");
                                if (returnNodes.Count > 0)
                                {
                                    parameterDocumentationMap.Add("returns", returnNodes[0].InnerText);
                                }

                                // TODO: Support for <remarks>, <seealso> tags needed. 
                                // Right now deletes all tags and leaves only contents of <summary>
                                if (modifyOperationDocumentation)
                                {
                                    var summary = "";
                                    var summaryNodes = xmlDoc.SelectNodes("root/summary");
                                    if (summaryNodes.Count > 0)
                                    {
                                        summary = summaryNodes[0].InnerText;
                                    }

                                    operation.Documentation = summary;
                                }

                                GenerateDocumentation_ForInputParameters(exporter, service, operation, parameterDocumentationMap);
                                GenerateDocumentation_ForOutputParameter(exporter, service, operation, parameterDocumentationMap);
                            }
                        }
                    }
                }
            }
        }

        private static void GenerateDocumentation_ForInputParameters(
            WsdlExporter exporter,
            System.Web.Services.Description.ServiceDescription service,
            Operation operation,
            Dictionary<string, string> parameterDocumentationMap)
        {
            var inputMsgName = operation.Messages.Input.Message.Name;

            var message = service.Messages
                .Cast<Message>()
                .FirstOrDefault(m => m.Name == inputMsgName);

            if (message.Parts.Count > 0)
            {
                var part = message.Parts[0];
                var elemName = part.Element?.Name;
                var elemNamespace = part.Element?.Namespace;

                var schema = exporter.GeneratedXmlSchemas
                    .Schemas()
                    .Cast<XmlSchema>()
                    .FirstOrDefault(s => s.TargetNamespace == elemNamespace);

                if (schema != null)
                {
                    var element = schema.Elements
                        .Values
                        .Cast<XmlSchemaElement>()
                        .FirstOrDefault(e => e.Name == elemName);

                    if (element != null)
                    {
                        foreach (var parameter in GetOperationParameter(element))
                        {
                            string documentation;
                            if (parameterDocumentationMap.TryGetValue(parameter.Name, out documentation))
                            {
                                parameter.Annotation = new XmlSchemaAnnotation();
                                parameter.Annotation.Items.Add(CreateDocumentationItem(documentation));
                            }
                        }
                    }
                }
            }
        }

        private static void GenerateDocumentation_ForOutputParameter(
            WsdlExporter exporter,
            System.Web.Services.Description.ServiceDescription service,
            Operation operation,
            Dictionary<string, string> parameterDocumentationMap)
        {
            var outputMsgName = operation.Messages.Output.Message.Name;

            var message = service.Messages
                .Cast<Message>()
                .FirstOrDefault(m => m.Name == outputMsgName);

            if (message.Parts.Count > 0)
            {
                var part = message.Parts[0];
                var elemName = part.Element?.Name;
                var elemNamespace = part.Element?.Namespace;

                var schema = exporter.GeneratedXmlSchemas
                    .Schemas()
                    .Cast<XmlSchema>()
                    .FirstOrDefault(s => s.TargetNamespace == elemNamespace);

                if (schema != null)
                {
                    var element = schema.Elements
                        .Values
                        .Cast<XmlSchemaElement>()
                        .FirstOrDefault(e => e.Name == elemName);

                    if (element != null)
                    {
                        var returnParameter = GetOperationParameter(element).FirstOrDefault();

                        if (returnParameter != null)
                        {
                            returnParameter.Annotation = new XmlSchemaAnnotation();
                            returnParameter.Annotation.Items.Add(CreateDocumentationItem(parameterDocumentationMap["returns"]));
                        }
                    }
                }
            }
        }

        private static IEnumerable<XmlSchemaElement> GetOperationParameter(XmlSchemaElement compositeElement)
        {
            var complexType = compositeElement.ElementSchemaType as XmlSchemaComplexType;
            if (complexType != null)
            {
                var contentTypeParticle = complexType.ContentTypeParticle as XmlSchemaSequence;
                if (contentTypeParticle != null)
                {
                    foreach (var item in contentTypeParticle.Items)
                    {
                        var elemItem = item as XmlSchemaElement;
                        yield return elemItem;
                    }
                }
            }
        }

        internal static void ExportContract(WsdlExporter exporter, WsdlContractConversionContext context, XmlCommentFormat format)
        {
            InitXsdDataContractExporter(exporter, format);

            XmlDocument commentsDoc = XmlCommentsUtils.LoadXmlComments(context.Contract.ContractType);
            if (commentsDoc == null)
                return;

            string comment = XmlCommentsUtils.GetFormattedComment(commentsDoc, context.Contract.ContractType, format);
            if (comment != null)
            {
                context.WsdlPortType.Documentation = comment;
            }

            foreach (Operation op in context.WsdlPortType.Operations)
            {
                OperationDescription opDescription = context.GetOperationDescription(op);

                MemberInfo mi = opDescription.SyncMethod;
                if (mi == null)
                    mi = opDescription.BeginMethod;
#if NET45
                if (mi == null)
                    mi = opDescription.TaskMethod;
#endif
                comment = XmlCommentsUtils.GetFormattedComment(commentsDoc, mi, format);
                if (comment != null)
                {
                    op.Documentation = comment;
                }
            }
        }
    }

    class XmlCommentsDataSurrogate : IDataContractSurrogate
    {
        IDataContractSurrogate prevSurrogate;
        XmlCommentFormat format;

        public XmlCommentsDataSurrogate(IDataContractSurrogate prevSurrogate)
        {
            this.prevSurrogate = prevSurrogate;
        }

        public XmlCommentsDataSurrogate(IDataContractSurrogate prevSurrogate, XmlCommentFormat format)
            : this(prevSurrogate)
        {
            this.format = format;
        }

        object IDataContractSurrogate.GetCustomDataToExport(Type clrType, Type dataContractType)
        {
            if (dataContractType.IsGenericType && dataContractType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                dataContractType = dataContractType.GetGenericArguments()[0];
            }
            XmlDocument commentsDoc = XmlCommentsUtils.LoadXmlComments(dataContractType);
            if (commentsDoc != null)
            {
                if (dataContractType.IsEnum)
                {
                    EnumAnnotation annotation = new EnumAnnotation();
                    string comment = XmlCommentsUtils.GetFormattedComment(commentsDoc, dataContractType, format);
                    if (comment != null)
                        annotation.EnumText = comment;
                    Dictionary<string, MemberInfo> enumMembers = ReflectionUtils.GetEnumMembers(dataContractType);
                    foreach (KeyValuePair<string, MemberInfo> member in enumMembers)
                    {
                        comment = XmlCommentsUtils.GetFormattedComment(commentsDoc, member.Value, format);
                        if (comment != null)
                        {
                            annotation.Members.Add(member.Key, comment);
                        }
                    }
                    if (annotation.EnumText != null || annotation.Members.Count > 0)
                        return annotation;
                }
                else
                {
                    string comment = XmlCommentsUtils.GetFormattedComment(commentsDoc, dataContractType, format);
                    if (comment != null)
                    {
                        return new Annotation(comment);
                    }
                }
            }
            return null;
        }

        object IDataContractSurrogate.GetCustomDataToExport(MemberInfo memberInfo, Type dataContractType)
        {
            XmlDocument commentsDoc = XmlCommentsUtils.LoadXmlComments(memberInfo.DeclaringType);
            if (commentsDoc != null)
            {
                string comment = XmlCommentsUtils.GetFormattedComment(commentsDoc, memberInfo, format);
                if (comment != null)
                {
                    return new Annotation(comment);
                }
            }
            return null;
        }

        Type IDataContractSurrogate.GetDataContractType(Type type)
        {
            if (prevSurrogate != null)
                return prevSurrogate.GetDataContractType(type);
            return type;
        }

        object IDataContractSurrogate.GetDeserializedObject(object obj, Type targetType)
        {
            if (prevSurrogate != null)
                return prevSurrogate.GetDeserializedObject(obj, targetType);
            return obj;
        }

        void IDataContractSurrogate.GetKnownCustomDataTypes(System.Collections.ObjectModel.Collection<Type> customDataTypes)
        {
            if (prevSurrogate != null)
                prevSurrogate.GetKnownCustomDataTypes(customDataTypes);
            customDataTypes.Add(typeof(Annotation));
            customDataTypes.Add(typeof(EnumAnnotation));
        }

        object IDataContractSurrogate.GetObjectToSerialize(object obj, Type targetType)
        {
            if (prevSurrogate != null)
                return prevSurrogate.GetObjectToSerialize(obj, targetType);
            return obj;
        }

        Type IDataContractSurrogate.GetReferencedTypeOnImport(string typeName, string typeNamespace, object customData)
        {
            if (prevSurrogate != null)
                return prevSurrogate.GetReferencedTypeOnImport(typeName, typeNamespace, customData);
            return null;
        }

        System.CodeDom.CodeTypeDeclaration IDataContractSurrogate.ProcessImportedType(System.CodeDom.CodeTypeDeclaration typeDeclaration, System.CodeDom.CodeCompileUnit compileUnit)
        {
            if (prevSurrogate != null)
                return prevSurrogate.ProcessImportedType(typeDeclaration, compileUnit);
            return typeDeclaration;
        }
    }

    [DataContract(Namespace = AnnotationNamespace)]
    class Annotation
    {
        public const string AnnotationNamespace = "XmlCommentsExporter.Annotation";

        public Annotation(string s)
        {
            this.Text = s;
        }
        [DataMember]
        public string Text;
    }

    [DataContract(Namespace = Annotation.AnnotationNamespace)]
    class EnumAnnotation
    {
        [DataMember]
        public string EnumText;

        [DataMember]
        public Dictionary<string, string> Members = new Dictionary<string, string>();
    }
}
