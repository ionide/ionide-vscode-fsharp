fsi.AddPrinter (fun (_: obj) ->
    let fsiAsm = "FSI-ASSEMBLY"

    let asmNum (asm:System.Reflection.Assembly) =
        asm.GetName().Name.Replace(fsiAsm, "") |> System.Int32.TryParse |> fun (b,v) -> if b then v else 0

    let fsiAssemblies =
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

    let formatVarsAndFuncs name value typ step shadowed =
        let name = if shadowed then (name + " (shadowed)") else name
        (sprintf "%s###IONIDESEP###%A###IONIDESEP###%s###IONIDESEP###%i" name value typ step).Replace("\n", ";")

    let formatRecsAndUnions name flds step shadowed =
        let name = if shadowed then (name + " (shadowed)") else name
        (sprintf "%s###IONIDESEP###%s###IONIDESEP###%i" name flds step).Replace("\n", ";")

    let fromAssemblies folder =
        fsiAssemblies
        |> Array.toList
        |> Seq.map (fun asm -> asmNum asm, asm)
        |> Seq.sortByDescending fst // assume later/higher assembly # always shadows
        |> Seq.fold folder (Set.empty, Seq.empty)
        |> snd

    let arrangeVars (state: Set<string> * seq<string>) (step, asm) =
        let varsWithNames =
            getWatchableVariables asm
            |> Seq.map (fun (name, value, typ) ->
                let shadowed = (fst state).Contains name
                formatVarsAndFuncs name value typ.Name step shadowed, name)
        let names =
            varsWithNames
            |> Seq.fold (fun (st:Set<string>) (_, nm) -> st.Add nm) (fst state)
            // add assembly names to lookup set

        names, Seq.append (snd state) (varsWithNames |> Seq.map fst)

    let arrangeFuncs (state: Set<string> * seq<string>) (step, asm) =
        let funcsWithNames =
            getFuncs asm
            |> Seq.map (fun (name, parms, typ) ->
                let shadowed = (fst state).Contains name
                let parms =
                    parms
                    |> Seq.map (fun (n, t) -> n + ": " + t)
                    |> String.concat "; "
                formatVarsAndFuncs name parms typ step shadowed, name)
        let names =
            funcsWithNames
            |> Seq.fold (fun (st:Set<string>) (_, nm) -> st.Add nm) (fst state)
            // add assembly names to lookup set

        names, Seq.append (snd state) (funcsWithNames |> Seq.map fst)

    let arrangeRecords (state: Set<string> * seq<string>) (step, asm) =
        let recsWithNames =
            getRecords asm
            |> Seq.map (fun (name, flds) ->
                let shadowed = (fst state).Contains name
                let f =
                    flds
                    |> Seq.map (fun (x, y) -> x + ": " + y)
                    |> String.concat "; "

                formatRecsAndUnions name f step shadowed, name)
        let names =
            recsWithNames
            |> Seq.fold (fun (st:Set<string>) (_, nm) -> st.Add nm) (fst state)
            // add assembly names to lookup set

        names, Seq.append (snd state) (recsWithNames |> Seq.map fst)

    let arrangeUnions (state: Set<string> * seq<string>) (step, asm) =
        let unionsWithNames =
            getUnions asm
            |> Seq.map (fun (name, flds) ->
                let shadowed = (fst state).Contains name
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

                formatRecsAndUnions name f step shadowed, name)
        let names =
            unionsWithNames
            |> Seq.fold (fun (st:Set<string>) (_, nm) -> st.Add nm) (fst state)
            // add assembly names to lookup set

        names, Seq.append (snd state) (unionsWithNames |> Seq.map fst)

    let writeToFile filename (lines:seq<string>) =
        let path = System.IO.Path.Combine(__SOURCE_DIRECTORY__, filename)
        System.IO.File.WriteAllText(path, lines |> String.concat "\n")

    let write content filename =
        async {
            try
                do content |> writeToFile filename
            with
            | _ -> ()
        }

    async {
        let types = Seq.append (fromAssemblies arrangeUnions) (fromAssemblies arrangeRecords)

        do! write (fromAssemblies arrangeVars) "vars.txt"
        do! write (fromAssemblies arrangeFuncs) "funcs.txt"
        do! write types "types.txt"
    }
    |> Async.Start

    null)
