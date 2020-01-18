fsi.AddPrinter (fun (_: obj) ->
    let getWatchableVariables () =
        let fsiAssembly =
            System.AppDomain.CurrentDomain.GetAssemblies()
            |> Seq.find (fun assm -> assm.GetName().Name = "FSI-ASSEMBLY")


        fsiAssembly.GetTypes()//FSI types have the name pattern FSI_####, where #### is the order in which they were created
        |> Seq.filter (fun ty -> ty.Name.StartsWith("FSI_"))
        |> Seq.sortBy (fun ty -> ty.Name.Split('_').[1] |> int)
        |> Seq.collect (fun ty ->
            ty.GetProperties()
            |> Seq.filter (fun pi -> pi.GetIndexParameters().Length > 0 |> not && pi.Name.Contains("@") |> not))
        |> Seq.mapi (fun i pi -> pi.Name, (i, pi)) //remember the order
        |> Map.ofSeq //remove leading duplicates (but now ordered by property name)
        |> Map.toSeq //reconstitue
        |> Seq.sortBy (fun (_,(i,_)) -> i) //order by original index
        |> Seq.map (fun (_,(_,pi)) -> pi.Name, pi.GetValue(null, Array.empty), pi.PropertyType) //discard ordering index, project usuable watch value

    let action =
        async {
            let vars =
                getWatchableVariables ()
                |> Seq.map (fun (name, value, typ) ->
                    sprintf "%s, %O, %s" name value typ.Name
                )
                |> String.concat "\n"
            let path = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "vars.txt")
            System.IO.File.WriteAllText(path, vars)
        }
    Async.Start action

    null
)