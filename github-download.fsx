// Adapted from Paket.BootStrapper - https://github.com/fsprojects/Paket/tree/master/src/Paket.Bootstrapper

open System
open System.IO
open System.Web
open System.Net
open System.Collections.Generic

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let getEnvVarValue (name:string) = 
    match Environment.GetEnvironmentVariable <| name.ToUpperInvariant() with
    | null -> 
        match Environment.GetEnvironmentVariable <| name.ToUpperInvariant() with
        | null -> None
        | v -> Some v
    | v -> Some v

let getBypassList () =
    match getEnvVarValue "NO_PROXY" with
    | None -> [||]
    | Some noproxy -> noproxy.Split([|';'|],StringSplitOptions.RemoveEmptyEntries)

let tryGetCredentials (uri:Uri) =
    let userPass = uri.UserInfo.Split([|':'|],2)
    if userPass.Length <> 2 && userPass.[0].Length <= 0 then None else
    Some <| NetworkCredential(Uri.UnescapeDataString userPass.[0], Uri.UnescapeDataString userPass.[1])

let addProxy (scheme:string) (bypassList:string []) (proxies:Dictionary<_,IWebProxy>) =
    let envVarName = sprintf "%s_PROXY" <| scheme.ToUpperInvariant()
    match getEnvVarValue envVarName with
    | None -> proxies
    | Some envVar ->
        match Uri.TryCreate(envVar,UriKind.Absolute) with
        | false, _ -> proxies
        | true, envUri -> 
            let proxy = WebProxy (sprintf "http://%s:%i" envUri.Host envUri.Port |> Uri)
            match tryGetCredentials envUri with
            | None -> () 
            | Some credentials -> proxy.Credentials <- credentials
            proxy.BypassProxyOnLocal <- true
            proxy.BypassList <- bypassList
            proxies.Add(scheme, proxy)
            proxies

let envProxy (proxies:Dictionary<string,IWebProxy>) =
    let bypassList = getBypassList()
    addProxy "http" bypassList proxies
    |> addProxy "https" bypassList
    
let tryGetProxyFor (uri:Uri) (proxies:Dictionary<string,IWebProxy>) =
    match proxies.TryGetValue uri.Scheme with
    | false, _ -> None
    | true, proxy -> Some proxy    

let tryGetDefaultWebProxyFor (url:string) proxies =
    let uri = Uri url
    match tryGetProxyFor uri proxies with
    | Some result when result.GetProxy uri <> uri -> Some result
    | _ ->
        let address = WebRequest.GetSystemWebProxy().GetProxy uri
        if address = uri then None else
        WebProxy(address,Credentials=CredentialCache.DefaultCredentials,BypassProxyOnLocal=true)
        :> IWebProxy |> Some
        
let [<Literal>] githubDownloader = "GithubDownloader"

let prepareWebClient  (url:string) proxies (client:WebClient)=
    client.Headers.Add("user-agent",githubDownloader)
    client.UseDefaultCredentials <- true
    match tryGetDefaultWebProxyFor url proxies with
    | Some proxy -> client.Proxy <- proxy; client
    | _ -> client

let prepareWebRequest (url:string) proxies =
    let request = HttpWebRequest.Create url :?> HttpWebRequest
    request.UserAgent <- githubDownloader
    request.UseDefaultCredentials <- true
    tryGetDefaultWebProxyFor url proxies
    |> Option.iter (fun proxy -> request.Proxy <- proxy)
    request.AutomaticDecompression <- DecompressionMethods.GZip ||| DecompressionMethods.Deflate
    request

         
type WebRequestProxy () =
    let proxies = Dictionary<string, IWebProxy>(StringComparer.OrdinalIgnoreCase) |> envProxy
    
    member val Client = new WebClient () with get, set
    
    member self.DownloadString (address:string) =
        self.Client <- prepareWebClient address proxies self.Client
        self.Client.DownloadString address


    member self.GetResponseStream (url:string) =
        let request = prepareWebRequest url proxies  
        use httpResponse = request.GetResponse()
        httpResponse.GetResponseStream()

        
    member self.DownloadFile (url:string) (targetLocation:string) =
        self.Client <- prepareWebClient url proxies self.Client
        self.Client.DownloadFile(url,targetLocation)

    
    member self.DownloadFileStream (url:string) (stream:Stream) (bufferSize:int) =
        let request = prepareWebRequest url proxies
        use httpResponse = request.GetResponse()
        use responseStream = httpResponse.GetResponseStream()
        responseStream.CopyTo(stream,bufferSize)


let getTempFile name =
    let path = Path.GetTempPath()
    let fileName = Path.Combine(path, name + string (System.Diagnostics.Process.GetCurrentProcess().Id))
    if File.Exists fileName then File.Delete fileName
    fileName


let releasesLatestUrl user repo = 
    sprintf "https://github.com/%s/%s/releases/latest" user repo

let releasesUrl user repo  = 
    sprintf "https://github.com/%s/%s/releases" user repo

let downloadUrl user repo release file = 
    sprintf "https://github.com/%s/%s/releases/download/%s/%s" user repo release file

let [<Literal>] HttpBufferSize = 4096

type GithubDownloader (user, repo) =
    let releases = releasesUrl user repo
    let latestReleases = releasesLatestUrl user repo
    
    let webProxy = WebRequestProxy ()
    
    member __.GetVersions (data:string) =
        let start = data.IndexOf(sprintf "%s/tree/" repo, 0)
        let rec loop (versions:string list) start =
            if start = -1 then versions else
            let start = start + 11
            let finish = data.IndexOf( "\"", start)
            let latestVersion = data.Substring (start,finish - start)
            match (List.contains latestVersion versions) with
            | false -> latestVersion::versions
            | true -> versions
        loop [] start |> List.rev


    member __.DownloadVersion latestVersion file path =
        let url = downloadUrl user repo latestVersion file
        printfn "Starting download of %s from %s" file url
        let tempFile = getTempFile file
        
        using (File.Create tempFile) (fun fileStream ->
            webProxy.DownloadFileStream url fileStream HttpBufferSize
        )
        let destination = Path.Combine(path,file)
        File.Copy(tempFile,destination , true)
        File.Delete tempFile
        printfn "Finished Download\n%s" destination


    member __.GetLatestStable() =
        let data = webProxy.DownloadString latestReleases
        let start = data.IndexOf "<title>" + 7
        let finish = data.IndexOf "</title>" + 8
        let title = data.Substring(start, finish - start)         
        (title.Split ' ').[1]

    
    member self.DownloadLatestStable file path =
        let version = self.GetLatestStable()
        self.DownloadVersion version file path
        

    member self.GetLatestPreRelease () =
        let data = webProxy.DownloadString releases
        match self.GetVersions data with 
        | hd::tl when hd.Contains "-" -> hd
        | _ -> String.Empty
        
    member self.DownloadLatest file path =
        match self.GetLatestPreRelease () with
        | "" -> self.DownloadLatestStable file path
        | version -> self.DownloadVersion version file path          

(*
;;
let ghDownload = GithubDownloader("fsprojects", "Forge")
ghDownload.DownloadLatest "forge.zip" __SOURCE_DIRECTORY__
*)         