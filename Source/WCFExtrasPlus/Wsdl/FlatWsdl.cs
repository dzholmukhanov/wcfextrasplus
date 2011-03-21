using System.Collections;
using System.Collections.Generic;
using System.ServiceModel.Description;
using System.Xml.Schema;
using System.Linq;
using ServiceDescription = System.Web.Services.Description.ServiceDescription;
using log4net;

namespace WCFExtrasPlus.Wsdl
{
	/// <summary>
	/// Originally developed by Thinktecture:
	/// http://weblogs.thinktecture.com/cweyer/2007/05/improving-wcf-interoperability-flattening-your-wsdl.html
	/// </summary>
    public class FlatWsdl
    {
		private static readonly ILog Log = LogManager.GetLogger(typeof(FlatWsdl));

		internal static void ExportEndpoint(WsdlExporter exporter)
        {
            XmlSchemaSet schemaSet = exporter.GeneratedXmlSchemas;

			if (Log.IsInfoEnabled)
			{
				Log.InfoFormat("Endpoint has {0} schemas", schemaSet.Count);
				Log.InfoFormat("Endpoint has {0} wsdl documents", exporter.GeneratedWsdlDocuments.Count);
			}

            foreach (ServiceDescription wsdl in exporter.GeneratedWsdlDocuments)
            {
				Log.InfoFormat("Parsing WSDL {0}", wsdl.Name);

				List<XmlSchema> importsList = new List<XmlSchema>();

                foreach (XmlSchema schema in wsdl.Types.Schemas)
                {
                    AddImportedSchemas(schema, schemaSet, importsList);
                }

                wsdl.Types.Schemas.Clear();

                foreach (XmlSchema schema in importsList)
                {
                    RemoveXsdImports(schema);
					if (Log.IsInfoEnabled)
					{
						Log.InfoFormat("Adding schema for namespaces: ");
						foreach (var ns in schema.Namespaces.ToArray())
							Log.InfoFormat("{0}: {1}", ns.Name, ns.Namespace);
					}
					wsdl.Types.Schemas.Add(schema);
                }
            }
        }

        private static void AddImportedSchemas(XmlSchema schema, XmlSchemaSet schemaSet, List<XmlSchema> importsList)
        {
            foreach (XmlSchemaImport import in schema.Includes)
            {
                ICollection realSchemas =
                    schemaSet.Schemas(import.Namespace);

                foreach (XmlSchema ixsd in realSchemas)
                {
                    if (!importsList.Contains(ixsd))
                    {
                        importsList.Add(ixsd);
                        AddImportedSchemas(ixsd, schemaSet, importsList);
                    }
                }
            }
        }

        private static void RemoveXsdImports(XmlSchema schema)
        {
            for (int i = 0; i < schema.Includes.Count; i++)
            {
                if (schema.Includes[i] is XmlSchemaImport)
                    schema.Includes.RemoveAt(i--);
            }
        }
        
    }
}