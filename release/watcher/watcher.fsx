fsi.AddPrinter (fun (_: obj) ->
    let fsiAsm = "FSI-ASSEMBLY"

    let asmNum (asm:System.Reflection.Assembly) =
        asm.GetName().Name.Replace(fsiAsm, "") |> System.Int32.TryParse |> fun (b,v) -> if b then v else 0

    let fsiAssemblies () =
        // use multiple assemblies (FSI-ASSEMBLY1, FSI-ASSEMBLY2...) if single isn't found
        let fsiAsms =
            System.AppDomain.CurrentDomain.GetAssemblies()
            |> Array.filter (fun asm -> asm.GetName().Name.StartsWith fsiAsm)
        fsiAsms
        |> Array.tryFind (fun asm -> asm.GetName().Name = fsiAsm)
        |> function
        | Some asm -> [| asm |]
        | None -> fsiAsms

    let getWatchableVariables (fsiAssembly:System.Reflection.Assembly) =
        fsiAssembly.GetTypes() //FSI types have the name pattern FSI_####, where #### is the order in which they were created
        |> Seq.filter (fun ty -> ty.Name.StartsWith("FSI_"))
        |> Seq.sortBy (fun ty -> ty.Name.Split('_').[1] |> int)
        |> Seq.collect (fun ty ->
            ty.GetProperties()
            |> Seq.filter (fun pi ->
                pi.GetIndexParameters().Length > 0 |> not
                && pi.Name.Contains("@") |> not))
        |> Seq.mapi (fun i pi -> pi.Name, (i, pi)) //remember the order
        |> Map.ofSeq //remove leading duplicates (but now ordered by property name)
        |> Map.toSeq //reconstitue
        |> Seq.sortBy (fun (_, (i, _)) -> i) //order by original index
        |> Seq.map (fun (_, (_, pi)) -> pi.Name, pi.GetValue(null, Array.empty), pi.PropertyType) //discard ordering index, project usuable watch value

    let getRecords (fsiAssembly:System.Reflection.Assembly) =
        fsiAssembly.GetTypes()
        |> Seq.filter (fun ty -> ty.FullName.StartsWith("FSI"))
        |> Seq.filter (Reflection.FSharpType.IsRecord)
        |> Seq.map (fun ty ->
            let flds =
                Reflection.FSharpType.GetRecordFields ty
                |> Seq.map (fun n -> n.Name, n.PropertyType.Name)

            ty.Name, flds)

    let getUnions (fsiAssembly:System.Reflection.Assembly) =
        fsiAssembly.GetTypes()
        |> Seq.filter (fun ty -> ty.FullName.StartsWith("FSI"))
        |> Seq.filter (Reflection.FSharpType.IsUnion)
        |> Seq.filter (fun ty -> ty.BaseType.Name = "Object") //find DU declaration not DU cases
        |> Seq.map (fun ty ->
            let flds =
                Reflection.FSharpType.GetUnionCases ty
                |> Seq.map (fun n ->
                    let props =
                        n.GetFields()
                        |> Seq.map (fun n -> n.Name, n.PropertyType.Name)

                    n.Name, props)

            ty.Name, flds)

    let getFuncs (fsiAssembly:System.Reflection.Assembly) =
        fsiAssembly.GetTypes()
        |> Seq.filter (fun ty -> ty.FullName.StartsWith("FSI"))
        |> Seq.filter (Reflection.FSharpType.IsModule)
        |> Seq.choose (fun ty ->
            let meth =
                ty.GetMethods()
                |> Seq.filter (fun m ->
                    m.IsStatic
                    && not (Seq.isEmpty (m.GetParameters()))
                    && m.Name <> "set_it")
                |> Seq.map (fun m ->
                    let parms =
                        m.GetParameters()
                        |> Seq.map (fun p ->
                            p.Name,
                            if p.ParameterType.IsGenericParameter then
                                "'" + p.ParameterType.Name
                            else
                                p.ParameterType.Name)

                    m.Name, parms, m.ReturnType.Name)

            if Seq.isEmpty meth then
                None
            else
                Some(meth))
        |> Seq.collect id

    let writeToFile filename (lines:seq<string>) =
        let path = System.IO.Path.Combine(__SOURCE_DIRECTORY__, filename)
        System.IO.File.WriteAllText(path, lines |> String.concat "\n")

    let formatVarsAndFuncs name value typ step =
        (sprintf "%s###IONIDESEP###%A###IONIDESEP###%s###IONIDESEP###%i" name value typ step).Replace("\n", ";")

    let formatRecsAndUnions name flds step =
        (sprintf "%s###IONIDESEP###%s###IONIDESEP###%i" name flds step).Replace("\n", ";")

    let arrangeVars fsiAssembly =
        let step = asmNum fsiAssembly
        getWatchableVariables fsiAssembly
        |> Seq.map (fun (name, value, typ) -> formatVarsAndFuncs name value typ.Name step)
        |> Seq.filter (not << System.String.IsNullOrWhiteSpace)

    let arrangeFuncs fsiAssembly =
        let step = asmNum fsiAssembly
        getFuncs fsiAssembly
        |> Seq.map (fun (name, parms, typ) ->
            let parms =
                parms
                |> Seq.map (fun (n, t) -> n + ": " + t)
                |> String.concat "; "

            formatVarsAndFuncs name parms typ step)
        |> Seq.filter (not << System.String.IsNullOrWhiteSpace)

    let arrangeRecords fsiAssembly =
        let step = asmNum fsiAssembly
        getRecords fsiAssembly
        |> Seq.map (fun (name, flds) ->
            let f =
                flds
                |> Seq.map (fun (x, y) -> x + ": " + y)
                |> String.concat "; "

            formatRecsAndUnions name f step)
        |> Seq.filter (not << System.String.IsNullOrWhiteSpace)

    let arrangeUnions fsiAssembly =
        let step = asmNum fsiAssembly
        getUnions fsiAssembly
        |> Seq.map (fun (name, flds) ->
            let f =
                flds
                |> Seq.map (fun (x, props) ->
                    let y =
                        props
                        |> Seq.map (fun (x, y) -> sprintf "(%s: %s)" x y)
                        |> String.concat " * "

                    if System.String.IsNullOrWhiteSpace y then
                        x
                    else
                        x + ": " + y)
                |> String.concat "#|#"

            formatRecsAndUnions name f step)
        |> Seq.filter (not << System.String.IsNullOrWhiteSpace)

    let allVars () =
        fsiAssemblies ()
        |> Array.toSeq
        |> Seq.map arrangeVars
        |> Seq.concat

    let allFuncs () =
        fsiAssemblies ()
        |> Array.toSeq
        |> Seq.map arrangeFuncs
        |> Seq.concat

    let allTypes () =
        let asms =
            fsiAssemblies () |> Array.toSeq
        let unions =
            asms
            |> Seq.map arrangeUnions
            |> Seq.concat
        let recs =
            asms
            |> Seq.map arrangeRecords
            |> Seq.concat

        Seq.append unions recs

    let writeAll fn filename =
        async {
            try
                do fn () |> writeToFile filename
            with
            | _ -> ()
        }

    async {
        do! writeAll allVars "vars.txt"
        do! writeAll allFuncs "funcs.txt"
        do! writeAll allTypes "types.txt"
    }
    |> Async.Start

    null)
