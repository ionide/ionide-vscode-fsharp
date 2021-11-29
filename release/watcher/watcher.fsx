fsi.AddPrinter (fun (_: obj) ->
    let fsiAssembly =
        System.AppDomain.CurrentDomain.GetAssemblies()
        |> Seq.find (fun assm -> assm.GetName().Name = "FSI-ASSEMBLY")


    let getWatchableVariables () =
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

    let getRecords () =
        fsiAssembly.GetTypes()
        |> Seq.filter (fun ty -> ty.FullName.StartsWith("FSI"))
        |> Seq.filter (Reflection.FSharpType.IsRecord)
        |> Seq.map (fun ty ->
            let flds =
                Reflection.FSharpType.GetRecordFields ty
                |> Seq.map (fun n -> n.Name, n.PropertyType.Name)

            ty.Name, flds)

    let getUnions () =
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

    let getFuncs () =
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

    let variablesAction =
        async {
            try
                let vars =
                    getWatchableVariables ()
                    |> Seq.map (fun (name, value, typ) ->
                        let x = sprintf "%s###IONIDESEP###%A###IONIDESEP###%s" name value typ.Name
                        x.Replace("\n", ";"))
                    |> String.concat "\n"

                let path = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "vars.txt")
                System.IO.File.WriteAllText(path, vars)
            with
            | _ -> ()
        }

    let funcsAction =
        async {
            try
                let vars =
                    getFuncs ()
                    |> Seq.map (fun (name, parms, typ) ->
                        let parms =
                            parms
                            |> Seq.map (fun (n, t) -> n + ": " + t)
                            |> String.concat "; "

                        let x = sprintf "%s###IONIDESEP###%s###IONIDESEP###%s" name parms typ
                        x.Replace("\n", ";"))
                    |> String.concat "\n"

                let path = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "funcs.txt")
                System.IO.File.WriteAllText(path, vars)
            with
            | _ -> ()
        }

    let typesAction =
        async {
            try
                let records =
                    getRecords ()
                    |> Seq.map (fun (name, flds) ->
                        let f =
                            flds
                            |> Seq.map (fun (x, y) -> x + ": " + y)
                            |> String.concat "; "

                        let x = sprintf "%s###IONIDESEP###%s" name f
                        x.Replace("\n", ";"))
                    |> String.concat "\n"

                let unions =
                    getUnions ()
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

                        let x = sprintf "%s###IONIDESEP###%s" name f
                        x.Replace("\n", ";"))
                    |> String.concat "\n"

                let path = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "types.txt")
                System.IO.File.WriteAllText(path, records + "\n" + unions)
            with
            | _ -> ()
        }


    Async.Start variablesAction
    Async.Start typesAction
    Async.Start funcsAction

    null)
