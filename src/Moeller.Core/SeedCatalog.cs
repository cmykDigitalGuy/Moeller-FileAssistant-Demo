using System.IO.Compression;
using System.Text;

namespace Moeller.Core;

internal record ItemPlan(string Description,int Quantity,string Components="Artwork",int[]? Allocations=null);
internal record FixturePlan(string Name,int Item=0,int Pages=1,int Width=612,int Height=792);
internal record JobPlan(string Customer,string Title,string Scenario,ItemPlan[] Items,FixturePlan[] Files);

public sealed partial class Engine
{
    private static JobPlan[] Catalog() =>
    [
        new("Acme Fictional","Business Card Order","01 / Single business card",[new("Business Cards",500)],[new("Business-Card-500.pdf",0,2,252,144)]),
        new("Northstar Fictional","Event Postcards","02 / Five distinct artwork designs",[new("Postcards",500,"Artwork",[100,100,100,100,100])],Enumerable.Range(1,5).Select(i=>new FixturePlan($"Postcard-V{i:00}-100.pdf",0,2,432,288)).ToArray()),
        new("Hillside Fictional","Community Mailer","03 / Split quantity: 400 plus 100",[new("Postcards",500,"Artwork",[400,100])],[new("Postcard-V01-400.pdf",0,2,432,288),new("Postcard-V02-100.pdf",0,2,432,288)]),
        new("Evergreen Fictional","Three Campaigns","04 / Three separate PPLAN items",[new("Postcard A",250),new("Postcard B",500),new("Postcard C",1000)],[new("Postcard-A.pdf",0,2,432,288),new("Postcard-B.pdf",1,2,432,288),new("Postcard-C.pdf",2,2,432,288)]),
        new("Lakeside Fictional","Seasonal Program","05 / Separate cover and text",[new("Saddle Booklet",300,"Cover + Text")],[new("Booklet-Cover.pdf",0,4),new("Booklet-Text.pdf",0,12)]),
        new("Stonebridge Fictional","Alumni Booklet","06 / One PDF supplies cover and text",[new("Saddle Booklet",400,"Cover + Text")],[new("Combined-Cover-And-Text.pdf",0,16)]),
        new("Acorn Fictional","Branding Package","07 / Mixed products in one job",[new("Business Cards",500),new("Postcards",250),new("Booklet",100)],[new("Business-Cards.pdf",0,2,252,144),new("Postcards.pdf",1,2,432,288),new("Booklet.pdf",2,12)]),
        new("Sunset Fictional","Annual Flyer","08 / Incorrect source filename",[new("Flat Flyer",500)],[new("INCORRECT-CUSTOMER-NAME.pdf")]),
        new("Maple Fictional","Safety Program","09 / Intentionally missing artwork",[new("Program Cover",200),new("Program Text",200)],[]),
        new("Bluebird Fictional","New Menu","10 / Assigned PDF modified afterward",[new("Menu",100)],[new("Original-Menu.pdf",0,2)]),
        new("Foxglove Fictional","Variable Print","11 / PPML ZIP, not an ordinary PDF",[new("Variable Postcards",1000)],[new("Demo-PPML-Package.zip")]),
        new("Brookfield Fictional","Tradeshow Set","12 / Three components under one item",[new("Display Kit",50,"Left Panel + Right Panel + Header")],[new("Left-Panel.pdf"),new("Right-Panel.pdf"),new("Header.pdf")]),
        new("Birch Fictional","Partial Press Run","13 / Postcards submitted; envelope pending",[new("Postcards",400),new("Envelopes",400)],[new("Postcards.pdf",0,2,432,288),new("Envelopes.pdf",1,1,684,297)]),
        new("Riverbend Fictional","Updated Brochure","14 / Replacement and restore exercise",[new("Trifold Brochure",200)],[new("Brochure-Original.pdf",0,2),new("Brochure-Replacement.pdf",0,2)]),
        new("Greenleaf Fictional","Unclear Upload","15 / Ambiguous relationship: choose manually",[new("Product A",200),new("Product B",200)],[new("Customer-File-Unknown.pdf")]),
        new("Fictional Demo Corporation","Corporate Package","16 / Cards, postcards, booklet, envelopes",[new("Business Cards",500),new("Postcards",750),new("Saddle Booklets",250),new("Envelopes",1000)],[new("Business-Cards.pdf",0,2,252,144),new("Postcards.pdf",1,2,432,288),new("Saddle-Booklet.pdf",2,16),new("Envelopes.pdf",3,1,684,297)])
    ];
    private void Seed()
    {
        var plans=Catalog();
        db.Atomic(()=>
        {
            for(int index=0;index<plans.Length;index++)
            {
                var plan=plans[index]; int jobId=index+1; string number=(41023+index).ToString();
                string folder=Path.Combine("archive","2026","10 October",$"{plan.Customer} - {plan.Title} - {number}");
                foreach(string name in new[]{"provided","production","previous versions","submissions"}) Directory.CreateDirectory(Full(Path.Combine(folder,name)));
                db.Execute("INSERT INTO jobs VALUES($0,$1,$2,$3,$4,$5)",jobId,number,plan.Customer,plan.Title,plan.Scenario,folder);
                for(int n=0;n<plan.Items.Length;n++)
                {
                    var item=plan.Items[n]; int itemId=jobId*100+n+1;
                    db.Execute("INSERT INTO items VALUES($0,$1,$2,$3,$4,$5)",itemId,jobId,(n+1).ToString("00"),item.Description,item.Quantity,item.Components);
                    int[] allocations=item.Allocations??[item.Quantity];
                    for(int v=0;v<allocations.Length;v++) db.Execute("INSERT INTO designs(item_id,number,quantity) VALUES($0,$1,$2)",itemId,v+1,allocations[v]);
                }
                foreach(var fixture in plan.Files)
                {
                    string path=Full(Path.Combine(folder,"provided",fixture.Name));
                    string detail=$"Fictional job {number}. {plan.Items[fixture.Item].Description}. {fixture.Name}. This artwork is for testing file relationships, not commercial printing.";
                    if(Path.GetExtension(path)==".zip")
                    {
                        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
                        using var writer=new StreamWriter(zip.CreateEntry("demo.ppml").Open(),Encoding.UTF8);
                        writer.Write("<?xml version=\"1.0\"?><PPML DemoOnly=\"true\"><COMMENT>Fictional package; no production PPML compatibility is claimed.</COMMENT></PPML>");
                    }
                    else PdfTools.CreateFixture(path,$"JOB {number} / {plan.Items[fixture.Item].Description}",detail,fixture.Pages,fixture.Width,fixture.Height);
                }
                File.WriteAllText(Full(Path.Combine(folder,"READ-ME.txt")),plan.Scenario+Environment.NewLine+"Use supplied files under provided. Assigned copies appear under production. Submitted copies appear under Simulated Outputs. All records are fictional.");
            }
        });
        var modified=Assign(new Assignment(Sources(10).Single(),Items(10)[0].Id,1,100,"Artwork",true)); SimulateModification(modified.Id);
        var partial=Assign(new Assignment(Sources(13).Single(p=>Path.GetFileName(p)=="Postcards.pdf"),Items(13)[0].Id,1,400,"Artwork",true));
        Submit(13,[partial.Id],false,true,false);
        Assign(new Assignment(Sources(14).Single(p=>Path.GetFileName(p)=="Brochure-Original.pdf"),Items(14)[0].Id,1,200,"Artwork",false));
        Log(null,"Demo initialized","Sixteen fictional jobs created. Jobs 10, 13 and 14 intentionally start with working-state examples.");
    }
}
