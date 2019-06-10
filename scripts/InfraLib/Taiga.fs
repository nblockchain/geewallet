
namespace FSX.Infrastructure

open System.Net
open System.Collections
open System.Collections.Generic
open System.Web.Script.Serialization

module Taiga =

    let private TAIGA_API_URL = "https://api.taiga.io/api/v1"

    let GetProjectIdBySlug authToken projectSlug: int =
        use webClient = new WebClient()
        webClient.Headers.Add "Content-Type: application/json"
        webClient.Headers.Add(sprintf "Authorization: Bearer %s" authToken)
        let response = webClient.DownloadString(sprintf "%s/projects/by_slug?slug=%s" TAIGA_API_URL projectSlug)
        let jsonResponseDict = JavaScriptSerializer().Deserialize<Dictionary<string,obj>> response
        let projectId =
            match jsonResponseDict.TryGetValue "id" with
            | true, (:? int as value) -> value
            | _ -> failwith ("JSON response didn't include a string 'id' element? " + response)
        projectId

    let NumberOfUserStoriesInIssueByProjectIdAndIssueId authToken (projectId:int) (refId): Option<int> =
        use webClient = new WebClient()
        webClient.Headers.Add "Content-Type: application/json"
        webClient.Headers.Add(sprintf "Authorization: Bearer %s" authToken)
        let response =
            try
                Some(webClient.DownloadString(sprintf "%s/issues/by_ref?ref=%d&project=%d" TAIGA_API_URL refId projectId))
            with
            | :? WebException as wex ->
                match wex.Response with
                | :? HttpWebResponse as webResponse ->
                    match webResponse.StatusCode with
                    | HttpStatusCode.NotFound -> None
                    | _ -> reraise()
                | _ -> reraise()
            | _ -> reraise()

        match response with
        | None -> None
        | Some(likelyJsonResponse) ->
            let jsonResponseDict = JavaScriptSerializer().Deserialize<Dictionary<string,obj>> likelyJsonResponse
            let generatedUserStories =
                match jsonResponseDict.TryGetValue "generated_user_stories" with
                | true, null -> 0
                | false, _ -> 0
                | true, (:? ArrayList as userStories) -> userStories.Count
                | true, elementWithUnexpectedType -> failwith(sprintf "Unexpected element of type %s" (elementWithUnexpectedType.GetType().FullName))
            Some(generatedUserStories)

    let GetAuthToken username password =
        use webClient = new WebClient()
        webClient.Headers.Add "Content-Type: application/json"
        let response = webClient.UploadString(sprintf "%s/auth" TAIGA_API_URL,
                                              sprintf "{ \"type\": \"normal\", \"username\": \"%s\", \"password\": \"%s\" }" username password)
        let jsonResponseDict = JavaScriptSerializer().Deserialize<Dictionary<string,obj>> response
        let authToken =
            match jsonResponseDict.TryGetValue "auth_token" with
            | true, (:? string as value) -> value
            | _ -> failwith ("JSON response didn't include a string 'auth_token' element? " + response)
        authToken

