let x = 1
let c = {| A = 1; B = "asd"; C = [1..10]; D = System.DateTime.Now |}

type D = {
    Q: int option
    W: System.DateTime array
    E: string
}

let v = {Q = Some 1; W = [| System.DateTime.Now |]; E = "asdasdasdasdsadsasdasdas"}

let p = System.DateTime.Now

let l = async {return ()}