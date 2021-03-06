﻿namespace MonoDevelop.FSharp
open System
open System.Collections.Generic
open System.Linq
open System.Threading
open System.Threading.Tasks
open MonoDevelop.Core
open MonoDevelop.Core.Text
open MonoDevelop.Components.MainToolbar
open MonoDevelop.Ide
open MonoDevelop.Ide.Gui
open Microsoft.FSharp.Compiler.SourceCodeServices
open Symbols

[<AutoOpen>]
module Accessibility =

  let inline getImage name = ImageService.GetIcon( name, Gtk.IconSize.Menu)

  let inline getImageFromAccessibility pub inter priv typeWithAccessibility =
    let accessibility = (^a : (member Accessibility : FSharpAccessibility) typeWithAccessibility)
    if accessibility.IsPublic then getImage pub
    elif accessibility.IsInternal then getImage inter
    else getImage priv 


module Search =

  let private filter tag (s:FSharpSymbolUse seq) =
    match tag with
    | "type" | "t" | "c" -> s |> Seq.filter (function | Class _ -> true | _ -> false)
    | "mod" -> s |> Seq.filter (function | Module _ -> true | _ -> false)
    | "s" ->   s |> Seq.filter (function | ValueType _ -> true | _ -> false)
    | "i" ->   s |> Seq.filter (function | Interface _ -> true | _ -> false)
    | "e" ->   s |> Seq.filter (function | Enum _ -> true | _ -> false)
    | "d" ->   s |> Seq.filter (function | Delegate _ -> true | _ -> false)
    | "u" ->   s |> Seq.filter (function | Union _ -> true | _ -> false)
    | "r" ->   s |> Seq.filter (function | Record _ -> true | _ -> false)
    | "member" | "m" -> s |> Seq.filter (function | Method _ -> true | _ -> false)
    | "p" ->   s |> Seq.filter (function | Property _ -> true | _ -> false)
    | "f" ->   s |> Seq.filter (function | Field _ -> true | _ -> false)
    | "ap" ->  s |> Seq.filter (function | ActivePattern _ -> true | _ -> false)
    | "op" ->  s |> Seq.filter (function | Operator _ -> true | _ -> false)
    | _ ->     s

  let byTag tag (items: FSharpSymbolUse seq) =
      let definitions = items |> Seq.filter (fun s -> s.IsFromDefinition)
      let filtered = definitions |> filter tag
      filtered

  let getAllProjectSymbols projectFile =
    async {
    let projectOptions = MDLanguageService.Instance.GetProjectCheckerOptions projectFile
    let! proj = MDLanguageService.Instance.ParseAndCheckProject projectOptions
    if not proj.HasCriticalErrors then
      let! allSymbols = proj.GetAllUsesOfAllSymbols()
      return allSymbols |> Array.toSeq
    else return Seq.empty }

  let byPattern (cache:Dictionary<_,_>) pattern symbols =

    let matchName (matcher:StringMatcher) (name:string) =
      if name = null then SearchCategory.MatchResult(false, -1)
      else
        match cache.TryGetValue(name) with
        | true, v -> v
        | false, _ ->
          let doesMatch, rank = matcher.CalcMatchRank (name)
          let savedMatch = SearchCategory.MatchResult (doesMatch, rank)
          cache.Add(name, savedMatch)
          savedMatch

    let matcher = StringMatcher.GetMatcher (pattern, false)

    // constructors have a display name of ( .ctor ) use the enclosing entities display name 
    let correctDisplayName (symbol:FSharpSymbolUse) = 
      match symbol with
      | Constructor c -> c.EnclosingEntity.DisplayName
      | _ -> symbol.Symbol.DisplayName

    symbols
    |> Seq.choose (fun s -> let matchres = matchName matcher (correctDisplayName s)
                            if matchres.Match then Some(s, matchres.Rank)
                            else None)

type SymbolSearchResult(match', matchedString, rank, symbol:FSharpSymbolUse) =
  inherit SearchResult(match', matchedString, rank)

  let simpleName =
    // constructors have a displaye name of ( .ctor ) use the enclosing entities display name
    match symbol with
    | Constructor c -> c.EnclosingEntity.DisplayName
    | _ -> symbol.Symbol.DisplayName

  let offsetAndLength =
    lazy Symbols.getOffsetAndLength simpleName symbol

  override x.SearchResultType =
    match symbol with
    | Record _ | Module _ | ValueType _ | Delegate _ | Union _  | Class _
    | Namespace _ | Interface _ | Enum _ | ActivePattern _ -> SearchResultType.Type

    | ActivePatternCase _ | Field _ | UnionCase _ | Property _
    | Event _ | Operator _ | Constructor _ | Function _ | Val _-> SearchResultType.Member
    | _ -> SearchResultType.Unknown

  override x.Description =
    let cat =
      match symbol with
      | Record _ -> "record"
      | Module _ -> "module"
      | ValueType _ -> "struct"
      | Delegate _ -> "delegate"
      | Union _ -> "union"
      | Class _ -> "class"
      | Namespace _ -> "namespace"
      | Interface _ -> "interface"
      | Enum _ -> "enum"
      //TODO: check if we can isolate F# specific types
      // | Type _ -> getImage "md-type"
      | ActivePattern _ -> "active pattern"
      | Field _ -> "field"
      | UnionCase _ -> "union case"
      | Property _ -> "property"
      | Event _ -> "event"
      | Operator _ -> "operator"
      | Constructor _ -> "constructor"
      | Method _ -> "method"
      | Function _ -> "function"
      | Val _ -> "val"
      | _ -> "symbol"
    sprintf "%s (file %s)" cat symbol.RangeAlternate.FileName

  override x.PlainText = simpleName

  override x.File = symbol.RangeAlternate.FileName
  override x.Icon =
    match symbol with
    | Record _ -> getImage "md-type"
    | Module _ -> getImage "md-module"
    | ValueType s -> s |> getImageFromAccessibility Stock.Struct.Name Stock.InternalStruct.Name Stock.PrivateStruct.Name
    | Delegate d -> d |> getImageFromAccessibility Stock.Delegate.Name Stock.InternalDelegate.Name Stock.PrivateDelegate.Name
    | Union _ -> getImage "md-type"
    | Class c -> c |> getImageFromAccessibility Stock.Class.Name Stock.InternalClass.Name Stock.PrivateClass.Name
    | Namespace _ -> getImage Stock.NameSpace.Name
    | Interface i -> i |> getImageFromAccessibility Stock.Interface.Name Stock.InternalInterface.Name Stock.PrivateInterface.Name
    | Enum e -> e |> getImageFromAccessibility Stock.Enum.Name Stock.InternalEnum.Name Stock.PrivateEnum.Name
    //TODO: check if we can isolate F# specific types
    // | Type _ -> getImage "md-type"
    | ActivePattern _ -> getImage "md-type"
    | Field f ->f |> getImageFromAccessibility Stock.Field.Name Stock.InternalField.Name Stock.PrivateField.Name
    | UnionCase _ -> getImage "md-type"
    | Property p -> p |> getImageFromAccessibility Stock.Property.Name Stock.InternalProperty.Name Stock.PrivateProperty.Name
    | Event e -> e |> getImageFromAccessibility Stock.Event.Name Stock.InternalEvent.Name Stock.PrivateEvent.Name
    | Operator _ -> getImage "md-fs-field"
    | Constructor c -> c |> getImageFromAccessibility Stock.Method.Name Stock.InternalMethod.Name Stock.PrivateMethod.Name
    | Function mfv ->
      if mfv.IsExtensionMember then mfv |> getImageFromAccessibility "md-extensionmethod" "md-internal-extensionmethod" "md-private-extensionmethod"
      elif mfv.IsMember then mfv |> getImageFromAccessibility Stock.Method.Name Stock.InternalMethod.Name Stock.PrivateMethod.Name
      else getImage "md-fs-field"
    | Val _ -> getImage "md-fs-field" //NOTE: Maybe make this a normal field icon?
    | _ -> getImage Stock.Event.Name

  override x.GetTooltipInformation(token) =
    Async.StartAsTask(SymbolTooltips.getTooltipInformation symbol, cancellationToken = token)

  override x.Offset =
    fst (offsetAndLength.Force())

  override x.Length = 
    snd (offsetAndLength.Force())

type ProjectSearchCategory() =
  inherit SearchCategory(GettextCatalog.GetString ("Solution"), sortOrder = SearchCategory.FirstCategory)

  //type, module, struct, interface, enum, delegate, union, record
  let typeTags = ["type"; "t"; "c"; "mod"; "s"; "i"; "e"; "d"; "u"; "r" ]

  //member, property, field, event, active pattern, operator
  let memberTags = ["member"; "m"; "p"; "f"; "evt"; "ap"; "op"]
  let tags = lazy (List.concat [typeTags; memberTags] |> List.toArray)

  override x.get_Tags() = tags.Force()

  override x.IsValidTag tag =
    typeTags |> List.contains tag || memberTags |> List.contains tag

  override x.GetResults(searchCallback, pattern, token) =

    let addResult (symbol:FSharpSymbolUse, rank) = 
      if token.IsCancellationRequested then ()
      else
        let sr = SymbolSearchResult(pattern.Pattern, symbol.Symbol.DisplayName, rank, symbol)
        searchCallback.ReportResult sr

    Task.Run(
      (fun () ->
          let allProjectFiles =
            IdeApp.Workspace.GetAllProjects()
            |> Seq.filter (fun p -> p.SupportedLanguages |> Array.contains "F#")
            |> Seq.map (fun p -> p.FileName.ToString())
          let cachingSearch = Search.byPattern (Dictionary<_,_>())
          async {for projFile in allProjectFiles do
                   let! allProjectSymbols = Search.getAllProjectSymbols projFile
                   let typeFilteredSymbols = Search.byTag pattern.Tag allProjectSymbols
                   let matchedSymbols = typeFilteredSymbols |> cachingSearch pattern.Pattern
                   matchedSymbols |> Seq.iter addResult }
          |> Async.Start ), token)