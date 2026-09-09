using System.Reflection;

internal static class AssistantChecks
{
    public static void Run()
    {
        var type=typeof(League.FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;
        var place=type.GetMethod("Placement",BindingFlags.Static|BindingFlags.NonPublic)!;
        Rectangle Place(Rectangle client,Rectangle screen,Size size)=>(Rectangle)place.Invoke(null,new object[]{client,screen,size})!;
        var screen=new Rectangle(0,0,1920,1080);
        var right=Place(new Rectangle(100,100,1000,700),screen,new Size(320,630));
        if(right.Left!=1106 || !screen.Contains(right))throw new Exception("Assistant should attach on the right when space permits");
        var left=Place(new Rectangle(800,100,1000,700),screen,new Size(320,630));
        if(left.Left!=474 || !screen.Contains(left))throw new Exception("Assistant should fall back to the left");
        var secondary=new Rectangle(-1280,0,1280,720);
        var clamped=Place(new Rectangle(-1280,0,1280,720),secondary,new Size(320,900));
        if(!secondary.Contains(clamped))throw new Exception("Assistant must stay inside a smaller secondary monitor");
        var skills=type.GetMethod("ParseSkills",BindingFlags.Static|BindingFlags.NonPublic)!;
        var data=Newtonsoft.Json.Linq.JObject.Parse("{\"passive\":{\"name\":\"Passive\",\"description\":\"First<br>Second\"},\"spells\":[{\"spellKey\":\"w\",\"name\":\"Shield\"},{\"spellKey\":\"q\",\"name\":\"Strike\"}]}");
        var parsed=Newtonsoft.Json.Linq.JArray.FromObject(skills.Invoke(null,new object[]{data})!);
        if(parsed.Count!=3 || (string?)parsed[1]?["Key"]!="Q" || (string?)parsed[2]?["Key"]!="W" || ((string?)parsed[0]?["Description"])?.Contains("<br>")==true)
            throw new Exception("Skill descriptions must preserve passive and map ability keys while removing markup");
        Console.WriteLine("PASS: assistant placement, left fallback, and monitor bounds.");
    }
}
