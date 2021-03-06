﻿namespace FSharp.MetadataFormat


#if INTERACTIVE
#r @"..\..\lib\FSharp.PowerPack.Metadata.dll"
#r "System.Xml.Linq.dll"
#r @"..\..\packages\RazorEngine.3.3.0\lib\net40\RazorEngine.dll"
#endif

open System
open System.Reflection
open System.Collections.Generic
open Microsoft.FSharp.Metadata
open FSharp.Patterns

open System.Xml.Linq

type Comment =
  { Blurb : string
    FullText : string }
  static member Empty =
    { Blurb = ""; FullText = "" }
  static member Create(blurb, full) = 
    { Blurb = blurb; FullText = full }

type MemberOrValue = 
  { Usage : int -> string 
    Modifiers : string list 
    TypeArguments : string list 
    Signature : string }
  member x.FormatUsage(maxLength) = x.Usage(maxLength)
  member x.FormatTypeArguments = String.concat ", " x.TypeArguments
  member x.FormatModifiers = String.concat " " x.Modifiers
  static member Create(usage, mods, typars, sign) =
    { Usage = usage; Modifiers = mods; TypeArguments = typars;
      Signature = sign }

type Member =
  { Name : string
    Details : MemberOrValue
    Comment : Comment }
  static member Create(name, details, comment) = 
    { Member.Name = name; Details = details; Comment = comment }

type Type = 
  { Name : string 
    UrlName : string
    Comment : Comment 

    Constructors : Member list
    InstanceMembers : Member list
    StaticMembers : Member list }
  static member Create(name, url, comment, ctors, inst, stat) = 
    { Type.Name = name; UrlName = url; Comment = comment;
      Constructors = ctors; InstanceMembers = inst; StaticMembers = stat }

type Module = 
  { Name : string 
    UrlName : string
    Comment : Comment
    
    ValuesAndFuncs : Member list
    TypeExtensions : Member list
    ActivePatterns : Member list }
  static member Create(name, url, comment, vals, exts, pats) = 
    { Module.Name = name; UrlName = url; Comment = comment 
      ValuesAndFuncs = vals; TypeExtensions = exts; ActivePatterns = pats }

type Namespace = 
  { Name : string
    Modules : Module list
    Types : Type list }
  static member Create(name, mods, typs) = 
    { Namespace.Name = name; Modules = mods; Types = typs }

type Assembly = 
  { Name : AssemblyName
    Namespaces : Namespace list }
  static member Create(name, nss) =
    { Assembly.Name = name; Namespaces = nss }

type ModuleInfo = 
  { Module : Module
    Assembly : Assembly }
  static member Create(modul, asm) = 
    { ModuleInfo.Module = modul; Assembly = asm }

type TypeInfo = 
  { Type : Type
    Assembly : Assembly }
  static member Create(typ, asm) = 
    { TypeInfo.Type = typ; Assembly = asm }

module ValueReader = 
  open System.Collections.ObjectModel

  let (|AllAndLast|_|) (list:'T list)= 
    if list.IsEmpty then None
    else let revd = List.rev list in Some(List.rev revd.Tail, revd.Head)

  let uncapitalize (s:string) =
    s.Substring(0, 1).ToLowerInvariant() + s.Substring(1)

  let isAttrib<'T> (attrib: FSharpAttribute)  =
    attrib.ReflectionType = typeof<'T> 

  let tryFindAttrib<'T> (attribs: ReadOnlyCollection<FSharpAttribute>)  =
    attribs |> Seq.tryPick (fun a -> if isAttrib<'T>(a) then Some (a.Value :?> 'T) else None)

  let hasAttrib<'T> (attribs: ReadOnlyCollection<FSharpAttribute>) = 
    tryFindAttrib<'T>(attribs).IsSome

  let (|MeasureProd|_|) (typ : FSharpType) = 
      if typ.IsNamed && typ.NamedEntity.LogicalName = "*" && typ.GenericArguments.Count = 2 then Some (typ.GenericArguments.[0], typ.GenericArguments.[1])
      else None

  let (|MeasureInv|_|) (typ : FSharpType) = 
      if typ.IsNamed && typ.NamedEntity.LogicalName = "/" && typ.GenericArguments.Count = 1 then Some typ.GenericArguments.[0]
      else None

  let (|MeasureOne|_|) (typ : FSharpType) = 
      if typ.IsNamed && typ.NamedEntity.LogicalName = "1" && typ.GenericArguments.Count = 0 then  Some ()
      else None

  let formatTypeArgument (typar:FSharpGenericParameter) =
    (if typar.IsSolveAtCompileTime then "^" else "'") + typar.Name

  let formatTypeArguments (typars:seq<FSharpGenericParameter>) =
    Seq.map formatTypeArgument typars |> List.ofSeq

  let bracket (str:string) = 
    if str.Contains(" ") then "(" + str + ")" else str

  let bracketIf cond str = 
    if cond then "(" + str + ")" else str

  let formatTyconRef (tcref:FSharpEntity) = 
    // TODO: layoutTyconRef generates hyperlinks 
    tcref.DisplayName

  let rec formatTypeApplication typeName prec prefix args =
    if prefix then 
      match args with
      | [] -> typeName
      | [arg] -> typeName + "<" + (formatTypeWithPrec 4 arg) + ">"
      | args -> bracketIf (prec <= 1) (typeName + "<" + (formatTypesWithPrec 2 "," args) + ">")
    else
      match args with
      | [] -> typeName
      | [arg] -> (formatTypeWithPrec 2 arg) + " " + typeName 
      | args -> bracketIf (prec <= 1) ((bracket (formatTypesWithPrec 2 "," args)) + typeName)

  and formatTypesWithPrec prec sep typs = 
    String.concat sep (typs |> Seq.map (formatTypeWithPrec prec))

  and formatTypeWithPrec prec (typ:FSharpType) =
    // Measure types are stored as named types with 'fake' constructors for products, "1" and inverses
    // of measures in a normalized form (see Andrew Kennedy technical reports). Here we detect this 
    // embedding and use an approximate set of rules for layout out normalized measures in a nice way. 
    match typ with 
    | MeasureProd (ty,MeasureOne) 
    | MeasureProd (MeasureOne, ty) -> formatTypeWithPrec prec ty
    | MeasureProd (ty1, MeasureInv ty2) 
    | MeasureProd (ty1, MeasureProd (MeasureInv ty2, MeasureOne)) -> 
        (formatTypeWithPrec 2 ty1) + "/" + (formatTypeWithPrec 2 ty2)
    | MeasureProd (ty1,MeasureProd(ty2,MeasureOne)) 
    | MeasureProd (ty1,ty2) -> 
        (formatTypeWithPrec 2 ty1) + "*" + (formatTypeWithPrec 2 ty2)
    | MeasureInv ty -> "/" + (formatTypeWithPrec 1 ty)
    | MeasureOne  -> "1" 
    | _ when typ.IsNamed -> 
        let tcref = typ.NamedEntity 
        let tyargs = typ.GenericArguments |> Seq.toList
        // layout postfix array types
        formatTypeApplication (formatTyconRef tcref) prec tcref.UsesPrefixDisplay tyargs 
    | _ when typ.IsTuple ->
        let tyargs = typ.GenericArguments |> Seq.toList
        bracketIf (prec <= 2) (formatTypesWithPrec 2 " * " tyargs)
    | _ when typ.IsFunction ->
        let rec loop soFar (typ:FSharpType) = 
          if typ.IsFunction then 
            let domainTyp, retType = typ.GenericArguments.[0], typ.GenericArguments.[1]
            loop (soFar + (formatTypeWithPrec 4 typ.GenericArguments.[0]) + " -> ") retType
          else 
            soFar + formatTypeWithPrec 5 typ
        bracketIf (prec <= 4) (loop "" typ)
    | _ when typ.IsGenericParameter ->
        formatTypeArgument typ.GenericParameter
    | _ -> "(type)" 

  let formatType typ = 
    formatTypeWithPrec 5 typ

  // Format each argument, including its name and type 
  let formatArgUsage generateTypes i (arg:FSharpParameter) = 
    // Detect an optional argument 
    let isOptionalArg = hasAttrib<OptionalArgumentAttribute> arg.Attributes
    let nm = match arg.Name with null -> "arg" + string i | nm -> nm
    let argName = if isOptionalArg then "?" + nm else nm
    if generateTypes then 
      (if String.IsNullOrWhiteSpace(arg.Name) then "" else argName + ":") + 
      formatTypeWithPrec 2 arg.Type
    else argName

  let formatArgsUsage generateTypes (v:FSharpMemberOrVal) args =
    let isItemIndexer = (v.IsInstanceMember && v.DisplayName = "Item")
    let counter = let n = ref 0 in fun () -> incr n; !n
    let unit, argSep, tupSep = 
      if generateTypes then "unit", " -> ", " * "
      else "()", " ", ", "
    args
    |> List.map (List.map (fun x -> formatArgUsage generateTypes (counter()) x))
    |> List.map (function 
        | [] -> unit 
        | [arg] when not v.IsMember || isItemIndexer -> arg 
        | args when isItemIndexer -> String.concat tupSep args
        | args -> bracket (String.concat tupSep args))
    |> String.concat argSep
  
  let readMemberOrVal (v:FSharpMemberOrVal) = 
    let buildUsage (args:string option) = 
      let tyname = v.LogicalEnclosingEntity.DisplayName
      let parArgs = args |> Option.map (fun s -> 
        if String.IsNullOrWhiteSpace(s) then "" 
        elif s.StartsWith("(") then s
        else sprintf "(%s)" s)
      match v.IsMember, v.IsInstanceMember, v.LogicalName, v.DisplayName with
      // Constructors and indexers
      | _, _, ".ctor", _ -> "new " + tyname
      | _, true, _, "Item" -> (uncapitalize tyname) + ".[" + (defaultArg args "...") + "]"
      // Ordinary instance members
      | _, true, _, name -> (uncapitalize tyname) + "." + name + (defaultArg parArgs "(...)")
      // Ordinary functions or values
      | false, _, _, name when 
          not (hasAttrib<RequireQualifiedAccessAttribute> v.LogicalEnclosingEntity.Attributes) -> 
            name + " " + (defaultArg args "(...)")
      // Ordinary static members or things (?) that require fully qualified access
      | _, _, _, name -> tyname + "." + name + (defaultArg parArgs "(...)")

    let modifiers =
      [ // TODO: v.Accessibility does not contain anything
        if v.InlineAnnotation = FSharpInlineAnnotation.AlwaysInline then yield "inline"
        if v.IsDispatchSlot then yield "abstract" ]

    let argInfos = v.CurriedParameterGroups |> Seq.map Seq.toList |> Seq.toList 
    let retType = v.ReturnParameter.Type
    let argInfos, retType = 
        match argInfos, v.IsGetterMethod, v.IsSetterMethod with
        | [ AllAndLast(args, last) ], _, true -> [ args ], Some last.Type
        | _, _, true -> argInfos, None
        | [[]], true, _ -> [], Some retType
        | _, _, _ -> argInfos, Some retType

    // Extension members can have apparent parents which are not F# types.
    // Hence getting the generic argument count if this is a little trickier
    let numGenericParamsOfApparentParent = 
        let pty = v.LogicalEnclosingEntity 
        if pty.IsExternal then 
            let ty = v.LogicalEnclosingEntity.ReflectionType 
            if ty.IsGenericType then ty.GetGenericArguments().Length 
            else 0 
        else 
            pty.GenericParameters.Count
    let tps = v.GenericParameters |> Seq.skip numGenericParamsOfApparentParent
    let typars = formatTypeArguments tps 

    //let cxs  = indexedConstraints v.GenericParameters 
    let retType = defaultArg (retType |> Option.map formatType) "unit"
    let signature =
      match argInfos with
      | [] -> retType
      | _  -> (formatArgsUsage true v argInfos) + " -> " + retType

    let usage = formatArgsUsage false v argInfos
    let buildShortUsage length = 
      let long = buildUsage (Some usage)
      if long.Length <= length then long
      else buildUsage None
    MemberOrValue.Create(buildShortUsage, modifiers, typars, signature)

    (*

    let docL = 
        let afterDocs = 
            [ let argCount = ref 0 
              for xs in argInfos do 
                for x in xs do 
                    incr argCount
                    yield layoutArgUsage true !argCount x

              if not v.IsGetterMethod && not v.IsSetterMethod && retType.IsSome then 
                  yield wordL "returns" ++ retTypeL
              match layoutConstraints denv () cxs with 
              | None ->  ()
              | Some cxsL -> yield cxsL ]
        match afterDocs with
        | [] -> emptyL
        | _ -> (List.reduce (@@) [ yield wordL ""; yield! afterDocs ])

    let noteL = 
        let noteDocs = 
            [ if cxs |> List.exists (snd >> List.exists (fun cx -> cx.IsMemberConstraint)) then
                  yield (wordL "Note: this operator is overloaded")  ]
        match noteDocs with
        | [] -> emptyL
        | _ -> (List.reduce (@@) [ yield wordL ""; yield! noteDocs ])
                
    let usageL = if v.IsSetterMethod then usageL --- wordL "<- v" else usageL
        
    //layoutAttribs denv v.Attributes 
    usageL  , docL, noteL
    *)

module Reader =
  open FSharp.Markdown
  open System.IO
  open ValueReader

  type ReadingContext = 
    { XmlMemberMap : IDictionary<string, XElement>
      MarkdownComments : bool
      UniqueUrlName : string -> string }
    member x.XmlMemberLookup(key) =
      match x.XmlMemberMap.TryGetValue(key) with
      | true, v -> Some v
      | _ -> None 
    static member Create(map) = 
      let usedNames = Dictionary<_, _>()
      let nameGen (name:string) =
        let nice = name.Replace(".", "-").Replace("`", "-").ToLower()
        let found =
          seq { yield nice
                for i in Seq.initInfinite id do yield sprintf "%s-%d" nice i }
          |> Seq.find (usedNames.ContainsKey >> not)
        usedNames.Add(found, true)
        found
      { XmlMemberMap = map; MarkdownComments = true; UniqueUrlName = nameGen }

  let removeSpaces (comment:string) =
    use reader = new StringReader(comment)
    let lines = 
      [ let line = ref ""
        while (line := reader.ReadLine(); line.Value <> null) do
          yield line.Value ]
    let spaces =
      lines 
      |> Seq.filter (String.IsNullOrWhiteSpace >> not)
      |> Seq.map (fun line -> line |> Seq.takeWhile Char.IsWhiteSpace |> Seq.length)
      |> Seq.min
    lines 
    |> Seq.map (fun line -> 
        if String.IsNullOrWhiteSpace(line) then ""
        else line.Substring(spaces))

  let readMarkdownComment (doc:MarkdownDocument) = 
    let groups = System.Collections.Generic.Dictionary<_, _>()
    let mutable current = "<default>"
    groups.Add(current, [])
    for par in doc.Paragraphs do
      match par with 
      | Heading(2, [Literal text]) -> 
          current <- text.Trim().ToLowerInvariant()
          groups.Add(current, [par])
      | par -> 
          groups.[current] <- par::groups.[current]
    let blurb = Markdown.WriteHtml(MarkdownDocument(List.rev groups.["<default>"], doc.DefinedLinks))
    let full = Markdown.WriteHtml(doc)
    Comment.Create(blurb, full)
          
  let readCommentAndCommands (ctx:ReadingContext) xmlSig = 
    match ctx.XmlMemberLookup(xmlSig) with 
    | None -> dict[], Comment.Empty
    | Some el ->
        let sum = el.Element(XName.Get "summary")
        if sum = null then dict[], Comment.Empty 
        else 
          if ctx.MarkdownComments then 
            let lines = removeSpaces sum.Value
            let cmds = new System.Collections.Generic.Dictionary<_, _>()
            let text =
              lines |> Seq.filter (function
                | String.StartsWithWrapped ("[", "]") (ParseCommands local, rest) -> 
                    for kvp in local do cmds.Add(kvp.Key, kvp.Value)
                    false
                | _ -> true) |> String.concat "\n"
            let doc = Markdown.Parse(text)
            cmds :> IDictionary<_, _>, readMarkdownComment doc
          else failwith "XML comments not supported yet"

  let readComment ctx xmlSig = readCommentAndCommands ctx xmlSig |> snd

  let readChildren ctx entities reader cond = 
    entities |> Seq.filter cond |> Seq.map (reader ctx) |> List.ofSeq

  let tryReadMember (ctx:ReadingContext) (memb:FSharpMemberOrVal) =
    let cmds, comment = readCommentAndCommands ctx memb.XmlDocSig
    if cmds.ContainsKey("omit") then None
    else Some(Member.Create(memb.DisplayName, readMemberOrVal memb, comment))

  let readAllMembers ctx (members:seq<FSharpMemberOrVal>) = 
    members 
    |> Seq.filter (fun v -> not v.IsCompilerGenerated)
    |> Seq.choose (tryReadMember ctx) |> List.ofSeq

  let readMembers ctx (entity:FSharpEntity) cond = 
    entity.MembersOrValues 
    |> Seq.filter (fun v -> not v.IsCompilerGenerated)
    |> Seq.filter cond |> Seq.choose (tryReadMember ctx) |> List.ofSeq

  let readType (ctx:ReadingContext) (typ:FSharpEntity) =
    let urlName = ctx.UniqueUrlName (sprintf "%s.%s" typ.Namespace typ.CompiledName)

    let ivals, svals = typ.MembersOrValues |> List.ofSeq |> List.partition (fun v -> v.IsInstanceMember)
    let cvals, svals = svals |> List.partition (fun v -> v.CompiledName = ".ctor")
    
    // Base types?
    let iimpls = 
      if ( not typ.IsAbbreviation && not typ.HasAssemblyCodeRepresentation && 
           typ.ReflectionType.IsInterface) then [] else typ.Implements |> List.ofSeq
    (*
    // TODO: layout base type in some way
    if not iimpls.IsEmpty then 
      newTable1 hFile "Interfaces" 40 "Type"  (fun () -> 
        iimpls |> List.iter (fun i -> 
            newEntry1 hFile ("<pre>"+outputL widthVal (layoutType denv i)+"</pre>"))) 
    *)
    let ctors = readAllMembers ctx cvals 
    let inst = readAllMembers ctx ivals 
    let stat = readAllMembers ctx svals 
    Type.Create
      ( typ.DisplayName, urlName, readComment ctx typ.XmlDocSig,
        ctors, inst, stat )

  let readModule (ctx:ReadingContext) (modul:FSharpEntity) =
    let urlName = ctx.UniqueUrlName (sprintf "%s.%s" modul.Namespace modul.CompiledName)
    let vals = readMembers ctx modul (fun v -> not v.IsMember && not v.IsActivePattern)
    let exts = readMembers ctx modul (fun v -> v.IsExtensionMember)
    let pats = readMembers ctx modul (fun v -> v.IsActivePattern)

    Module.Create
      ( modul.DisplayName, urlName, readComment ctx modul.XmlDocSig,
        vals, exts, pats )

  let readNamespace ctx (ns, entities:seq<FSharpEntity>) =
    let modules = readChildren ctx entities readModule (fun x -> x.IsModule) 
    let types = readChildren ctx entities readType (fun x -> not x.IsModule) 
    Namespace.Create(ns, modules, types)

  let readAssembly (assembly:FSharpAssembly) (xmlFile:string) =
    let assemblyName = assembly.ReflectionAssembly.GetName()
    
    // Read in the supplied XML file, map its name attributes to document text 
    let doc = XDocument.Load(xmlFile)
    let xmlMemberMap =
      [ for e in doc.Descendants(XName.Get "member") do
          let attr = e.Attribute(XName.Get "name") 
          if attr <> null && not (String.IsNullOrEmpty(attr.Value)) then 
            yield attr.Value, e ] |> dict
    let ctx = ReadingContext.Create(xmlMemberMap)

    // 
    let namespaces = 
      assembly.Entities 
      |> Seq.groupBy (fun m -> m.Namespace) |> Seq.sortBy fst
      |> Seq.map (readNamespace ctx) |> List.ofSeq
    Assembly.Create(assemblyName, namespaces)

open System.IO
open RazorEngine
open RazorEngine.Text
open RazorEngine.Templating
open RazorEngine.Configuration

module Global = 
  let mutable layoutRootGlobal = ""

[<AbstractClass>]
type DocPageTemplateBase<'T>() =
  inherit RazorEngine.Templating.TemplateBase<'T>()
  member val Title : string = "" with get,set
  member x.RenderPart(name, model:obj) =  
    let layoutsRoot = Global.layoutRootGlobal
    let partFile = Path.Combine(layoutsRoot, name + ".cshtml")
    if not (File.Exists(partFile)) then
      failwithf "Could not find template file: %s\nSearching in: %s" name layoutsRoot    
    Razor.Parse(File.ReadAllText(partFile), model)

type Html private() =
  static let mutable uniqueNumber = 0
  static member UniqueID() = 
    uniqueNumber <- uniqueNumber + 1
    uniqueNumber
  static member Encode(str) = 
    System.Web.HttpUtility.HtmlEncode(str)

module Formatter = 

  type RazorRender(layoutsRoot) =
    let config = new TemplateServiceConfiguration()
    do Global.layoutRootGlobal <- layoutsRoot
    do config.EncodedStringFactory <- new RawStringFactory()
    do config.Resolver <- 
        { new ITemplateResolver with
            member x.Resolve name =
              let layoutFile = Path.Combine(layoutsRoot, name + ".cshtml")
              if File.Exists(layoutFile) then File.ReadAllText(layoutFile)
              else failwithf "Could not find template file: %s\nSearching in: %s" name layoutsRoot }
    do config.Namespaces.Add("FSharp.MetadataFormat") |> ignore
    do config.BaseTemplateType <- typedefof<DocPageTemplateBase<_>>
    do config.Debug <- true        
    let templateservice = new TemplateService(config)
    do Razor.SetTemplateService(templateservice)

    member val Model : obj = obj() with get, set
    member val ViewBag = new DynamicViewBag() with get,set

    member x.ProcessFile(source) = 
      try
        x.ViewBag <- new DynamicViewBag()
        let html = Razor.Parse(File.ReadAllText(source), x.Model, x.ViewBag, null)
        html
      with :? TemplateCompilationException as ex -> 
        let csharp = Path.GetTempFileName() + ".cs"
        File.WriteAllText(csharp, ex.SourceCode)
        failwithf "Processing the file '%s' failed with exception:\n%O\nSource written to: '%s'." source ex csharp

open System.IO

type MetadataFormat = 
  static member Generate(dllFile, outDir, layoutRoot, ?namespaceTemplate, ?moduleTemplate, ?typeTemplate, ?xmlFile) =
    let (/) a b = Path.Combine(a, b)
    let xmlFile = defaultArg xmlFile (Path.ChangeExtension(dllFile, ".xml"))
    if not (File.Exists xmlFile) then 
      raise <| FileNotFoundException(sprintf "Associated XML file '%s' was not found." xmlFile)

//    [ for e in fasm.Entities do
//        for m in e.MembersOrValues do
//          yield m.IsTypeFunction ]
//          yield m.GenericParameters.Count ]

    
    let namespaceTemplate = defaultArg namespaceTemplate "namespaces.cshtml"
    let moduleTemplate = defaultArg moduleTemplate "module.cshtml"
    let typeTemplate = defaultArg typeTemplate "type.cshtml"
    
    let asm = FSharpAssembly.FromFile(dllFile)
    let asm = Reader.readAssembly asm xmlFile
    
    let razor = Formatter.RazorRender(layoutRoot)
    razor.Model <- box asm
    let out = razor.ProcessFile(layoutRoot / namespaceTemplate)
    File.WriteAllText(outDir / "index.html", out)

    let razor = Formatter.RazorRender(layoutRoot)
    for ns in asm.Namespaces do
      for modul in ns.Modules do
        razor.Model <- box (ModuleInfo.Create(modul, asm))
        let out = razor.ProcessFile(layoutRoot / moduleTemplate)
        File.WriteAllText(outDir / (modul.UrlName + ".html"), out)

    let razor = Formatter.RazorRender(layoutRoot)
    for ns in asm.Namespaces do
      for typ in ns.Types do
        razor.Model <- box (TypeInfo.Create(typ, asm))
        let out = razor.ProcessFile(layoutRoot / typeTemplate)
        File.WriteAllText(outDir / (typ.UrlName + ".html"), out)

// let dllFile = @"C:\dev\FSharp.DataFrame\bin\FSharp.DataFrame.dll"
// let layoutRoot = @"C:\dev\FSharp.Formatting\src\FSharp.MetadataFormat\templates"