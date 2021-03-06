﻿[<ReflectedDefinition>]
module TypeInferred.HashBang.Provider.Runtime

open System
open System.Net
open System.Text
open TypeInferred.HashBang.Runtime

module Serialization =

    let mutable serializers = Map.empty<string, obj -> string>
    let mutable deserializers = Map.empty<string, string -> obj>

    let getSerializer(t : Type) =
        match serializers.TryFind t.FullName with
        | Some serialize -> serialize
        | None ->
            let serialize = precomputeTypeToJson t
            serializers <- serializers.Add(t.FullName, serialize)
            serialize

    let getDeserializer(t : Type) =
        match deserializers.TryFind t.FullName with
        | Some deserialize -> deserialize
        | None ->
            let deserialize = precomputeTypeFromJson t
            deserializers <- deserializers.Add(t.FullName, deserialize)
            deserialize

    let serialize t obj = getSerializer t obj
    let deserialize t obj = getDeserializer t obj
        

type ApiDataContext =
    {
        BaseUrl : string
    }

type ApiRequest(httpMethod, urlParts : UrlPart[]) =
    let mutable headers = Map.empty<string, string>
    let mutable parameters = Map.empty<string, string>
    let mutable queryParameters = Map.empty<string, string>
    let mutable body = None

    member __.AddHeader(key, value) = 
        headers <- headers.Add(key, value)

    member __.AddParameter(key, value) =
        parameters <- parameters.Add(key, value)

    member __.AddQueryParameter(key, value) =
        queryParameters <- queryParameters.Add(key, value)

    member __.AddBody v = body <- Some v

    member __.Headers = headers

    member __.BuildUrl(dc : ApiDataContext) =
        dc.BaseUrl + (
            urlParts |> Array.map (fun part ->
                match part with
                | FixedPart section -> section
                | VariablePart(name, _) -> parameters.[name])
            |> String.concat "/")
    
    member req.Send(dc) =
        async {
            //TODO: Set content-type / read content-type
            let httpReq = WebRequest.Create(req.BuildUrl dc)
            httpReq.Method <- httpMethod
            headers |> Map.iter (fun key value ->
                httpReq.Headers.Add(key, value))
            match body with
            | None -> ()
            | Some(bodyText : string) ->
                use input = httpReq.GetRequestStream()
                let inBytes = Encoding.UTF8.GetBytes bodyText
                do! input.AsyncWrite inBytes
            use! httpRsp = httpReq.AsyncGetResponse()
            if httpRsp.ContentLength > 0L then
                use outputStream = httpRsp.GetResponseStream()
                // Assumption: less than int32 bytes of content
                let! outBytes = outputStream.AsyncRead (int httpRsp.ContentLength)
                let outText = Encoding.UTF8.GetString outBytes
                return Some outText
            else
                return None
        }


    member req.SendIgnore(dc) =
        async {
            let! _ = req.Send(dc)
            return ()
        }

    member req.SendAndDeserialize(t, dc) =
        async { 
            let! result = req.Send(dc)
            match result with
            | None -> return failwith "The response had no content."
            | Some json -> return Serialization.deserialize t json
        }