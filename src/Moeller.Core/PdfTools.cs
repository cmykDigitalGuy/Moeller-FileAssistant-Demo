using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PdfSharp.Pdf.IO;

namespace Moeller.Core;

public static class PdfTools
{
    public const string Namespace = "urn:moeller:file-assistant:1";
    private static readonly XNamespace Mp = Namespace;
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    public static string ExifToolPath => Environment.GetEnvironmentVariable("MOELLER_DEMO_EXIFTOOL") ?? System.IO.Path.Combine(AppContext.BaseDirectory,"tools","exiftool.exe");

    public static void Stamp(string source,string target,Identity identity)
    {
        if(!File.Exists(ExifToolPath))throw new FileNotFoundException("The bundled PDF metadata tool is missing. Repair the installed application.",ExifToolPath);
        XDocument packet;
        using(var doc=PdfReader.Open(source,PdfDocumentOpenMode.Import))
        {
            if(doc.PageCount==0)throw new InvalidDataException("PDF contains no pages.");
            if(doc.SecuritySettings.IsEncrypted)throw new InvalidDataException("Encrypted PDFs must be supplied without encryption for this demo.");
            var old=doc.Internals.Catalog.Elements.GetDictionary("/Metadata");
            try
            {
                packet=old?.Stream==null?NewPacket():Parse(Encoding.UTF8.GetString(old.Stream.UnfilteredValue));
                if(packet.Descendants(Rdf+"RDF").FirstOrDefault()==null)packet=NewPacket();
            }
            catch(XmlException){packet=NewPacket();}
        }
        foreach(var element in packet.Descendants().Where(e=>e.Name.Namespace==Mp).ToList())element.Remove();
        foreach(var attribute in packet.Descendants().Attributes().Where(a=>a.Name.Namespace==Mp).ToList())attribute.Remove();
        var description=new XElement(Rdf+"Description",new XAttribute(Rdf+"about",""),new XAttribute(XNamespace.Xmlns+"MOELLER_XMP",Mp));
        void Add(string key,object value)=>description.Add(new XElement(Mp+key,value));
        Add("FileID",identity.FileID);Add("PhysicalRevisionID",identity.PhysicalRevisionID);Add("JobNumber",identity.JobNumber);Add("ItemNumber",identity.ItemNumber);
        Add("ArtworkVersion",identity.ArtworkVersion);Add("Revision",identity.Revision);Add("Component",identity.Component);Add("SourceSHA256",identity.SourceSHA256);Add("MetadataSchemaVersion","1");Add("DemoOnly","true");
        packet.Descendants(Rdf+"RDF").First().Add(description);
        string xmp=target+".xmp",args=target+".args";
        File.Copy(source,target,false);
        try
        {
            File.WriteAllText(xmp,packet.ToString(SaveOptions.DisableFormatting),new UTF8Encoding(false));
            // UTF-8 argument files avoid Windows command-line encoding problems.
            // No shell is involved. Only the staged working copy is modified.
            File.WriteAllLines(args,new[]{"-overwrite_original","-charset","filename=utf8","-XMP<="+xmp,target},new UTF8Encoding(false));
            var start=new ProcessStartInfo(ExifToolPath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            start.ArgumentList.Add("-config");start.ArgumentList.Add("");start.ArgumentList.Add("-@");start.ArgumentList.Add(args);
            using var process=Process.Start(start)??throw new IOException("The PDF metadata tool could not start.");
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(30000)){try{process.Kill(true);}catch{}throw new IOException("PDF metadata processing exceeded the 30-second safety limit.");}
            string detail=(output.GetAwaiter().GetResult()+" "+error.GetAwaiter().GetResult()).Trim();
            if(process.ExitCode!=0)throw new IOException("PDF metadata stamping failed: "+detail);
            var readback=ReadIdentity(target);
            if(readback?.PhysicalRevisionID!=identity.PhysicalRevisionID||readback.FileID!=identity.FileID)throw new IOException("Embedded PDF identity did not pass independent read-back verification.");
        }
        finally{if(File.Exists(xmp))File.Delete(xmp);if(File.Exists(args))File.Delete(args);}
    }
    public static Identity? ReadIdentity(string path)
    {
        using var doc=PdfReader.Open(path,PdfDocumentOpenMode.Import);
        var metadata=doc.Internals.Catalog.Elements.GetDictionary("/Metadata");if(metadata?.Stream==null)return null;
        var packet=Parse(Encoding.UTF8.GetString(metadata.Stream.UnfilteredValue));
        string Value(string key)=>packet.Descendants(Mp+key).FirstOrDefault()?.Value??"";
        if(Value("FileID")==""||!int.TryParse(Value("ArtworkVersion"),out int version)||!int.TryParse(Value("Revision"),out int revision))return null;
        return new Identity(Value("FileID"),Value("PhysicalRevisionID"),Value("JobNumber"),Value("ItemNumber"),version,revision,Value("Component"),Value("SourceSHA256"));
    }
    private static XDocument Parse(string value)
    {
        using var reader=XmlReader.Create(new StringReader(value),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4_000_000});return XDocument.Load(reader);
    }
    private static XDocument NewPacket()
    {
        XNamespace x="adobe:ns:meta/";
        return new XDocument(new XElement(x+"xmpmeta",new XAttribute(XNamespace.Xmlns+"x",x),new XElement(Rdf+"RDF",new XAttribute(XNamespace.Xmlns+"rdf",Rdf))));
    }
    public static void CreateFixture(string path,string title,string details,int pages=1,int width=612,int height=792)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        static string Esc(string s)=>new string(s.Select(c=>c<=126&&c>=32?c:' ').ToArray()).Replace("\\","\\\\").Replace("(","\\(").Replace(")","\\)");
        string N(double n)=>n.ToString("0.###",CultureInfo.InvariantCulture);
        var objects=new List<string>{"<< /Type /Catalog /Pages 2 0 R >>","","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"};var kids=new List<string>();
        for(int page=1;page<=pages;page++)
        {
            int pageId=objects.Count+1,streamId=pageId+1;kids.Add($"{pageId} 0 R");
            double margin=Math.Min(30,width*.07),head=Math.Min(92,height*.28),fs=width<350?9:18,body=width<350?7:12;
            string Text(double x,double y,double size,string text,string color="0.06 0.1 0.17")=>$"{color} rg BT /F1 {N(size)} Tf {N(x)} {N(y)} Td ({Esc(text)}) Tj ET\n";
            var content=new StringBuilder();content.Append($"0.06 0.1 0.17 rg 0 {N(height-head)} {width} {N(head)} re f\n");
            content.Append(Text(margin,height-head*.45,fs,"MOELLER / FICTIONAL ARTWORK","1 1 1"));
            content.Append(Text(margin,height-head*.8,body,title.Length>66?title[..66]:title,"0.43 0.89 0.83"));
            double y=height-head-margin;int max=Math.Max(20,(int)((width-2*margin)/(body*.57)));
            foreach(string raw in new[]{details,$"Page {page} of {pages}","OFFLINE DEMO - NOT A REAL CUSTOMER ORDER"})
            {
                string rest=raw;
                while(rest.Length>0&&y>margin+body*3)
                {
                    int length=Math.Min(max,rest.Length);if(length<rest.Length&&rest.LastIndexOf(' ',length-1,length)>10)length=rest.LastIndexOf(' ',length-1,length);
                    content.Append(Text(margin,y,body,rest[..length]));rest=rest[length..].TrimStart();y-=body*1.5;
                }
                y-=body*.4;
            }
            content.Append($"0.0 0.48 0.46 rg {N(margin)} {N(margin)} {N(width-2*margin)} {N(Math.Max(4,height*.025))} re f\n");
            string stream=content.ToString();
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /Font << /F1 3 0 R >> >> /Contents {streamId} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream");
        }
        objects[1]=$"<< /Type /Pages /Count {pages} /Kids [{string.Join(" ",kids)}] >>";
        using var output=new MemoryStream();void Write(string s){var bytes=Encoding.ASCII.GetBytes(s);output.Write(bytes);}
        Write("%PDF-1.7\n%Moeller fictional fixture\n");var offsets=new List<long>{0};
        for(int i=0;i<objects.Count;i++){offsets.Add(output.Position);Write($"{i+1} 0 obj\n{objects[i]}\nendobj\n");}
        long start=output.Position;Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");foreach(long off in offsets.Skip(1))Write($"{off:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n");File.WriteAllBytes(path,output.ToArray());
    }
}
