"use strict";
const $=id=>document.getElementById(id), levels=["Легка","Середня","Складна","Експерт"];
let token=sessionStorage.getItem("token")||"", game=null, selected=0, note=false, online=false, busy=false, tab="play", epoch=0;
const message=s=>$("status").textContent=s;
if(localStorage.getItem("theme")==="light")document.documentElement.classList.add("light");
$("theme").onclick=()=>{document.documentElement.classList.toggle("light");localStorage.setItem("theme",document.documentElement.classList.contains("light")?"light":"dark")};
async function api(path,body){
 const controller=new AbortController(), timeout=setTimeout(()=>controller.abort(),path==='web/new'?30000:5000);
 try{const r=await fetch("/api/"+path,{method:body===undefined?"GET":"POST",headers:{"Content-Type":"application/json","Authorization":"Bearer "+token},body:body===undefined?undefined:JSON.stringify(body),signal:controller.signal});
 if(r.status===401){token="";sessionStorage.removeItem("token");showAuth();throw Error("Увійдіть повторно.");}
 if(!r.ok){const problem=await r.json().catch(()=>({}));throw Error(problem.error||problem.detail||(r.status===409?"Сеанс змінено в іншій вкладці.":"Помилка сервера: "+r.status));}
 return await r.json().catch(()=>null);
 }finally{clearTimeout(timeout)}
}
function showAuth(){epoch++;game=null;online=false;$("auth").hidden=false;$("app").hidden=true;$("logout").hidden=true}
async function login(register){
 try{let a=await api(register?"register":"login",{user:$("user").value,password:$("password").value});token=a.token;sessionStorage.setItem("token",token);$("password").value="";await start();}catch(e){message(e.message)}
}
$("login").onsubmit=e=>{e.preventDefault();login(false)};$("register").onclick=()=>login(true);
$("logout").onclick=async()=>{try{if(game&&!game.paused&&!game.completed)await act("pause");await api("logout",{})}catch{}token="";sessionStorage.removeItem("token");showAuth()};
async function start(){$("auth").hidden=true;$("app").hidden=false;$("logout").hidden=false;await reconnect()}
function accept(data){game=data?.empty?null:data;online=navigator.onLine;if(game)selected=Math.max(0,Math.min(80,selected));render()}
async function reconnect(){
 if(busy||!token)return;const e=epoch;busy=true;
 try{const data=await api("web/session");if(e!==epoch)return;accept(data);if(game&&!game.paused&&!game.completed){const paused=await api("web/action",{id:game.id,revision:game.revision,action:"pause"});if(e!==epoch)return;accept(paused);}message("З’єднання встановлено. Продовжіть гру.");}
 catch(e){disconnect(e)}finally{busy=false;render()}
}
function disconnect(e){online=false;message("Гру призупинено: "+e.message+". Очікуємо з’єднання.");render()}
window.addEventListener("offline",()=>disconnect(Error("немає мережі")));
window.addEventListener("online",reconnect);
async function act(action,extra={}){
 if(!online||busy||!game)return;const e=epoch;busy=true;
 try{const data=await api("web/action",{id:game.id,revision:game.revision,action,...extra});if(e!==epoch)return;accept(data);if(action==="hint")selected=game.selected;}
 catch(e){disconnect(e)}finally{busy=false;render()}
}
setInterval(()=>{if(!token||busy)return;if(!online)reconnect();else if(game)act("heartbeat")},3000);
function confirmNew(){return new Promise(resolve=>{const d=$("confirm");d.returnValue="cancel";d.addEventListener("close",()=>resolve(d.returnValue==="yes"),{once:true});d.showModal()})}
$("newgame").onclick=async()=>{if(!online||busy)return;if(game&&!game.completed&&!(await confirmNew()))return;if(!online||busy)return;const e=epoch;busy=true;render();message("Генерація головоломки…");
 try{const data=await api("web/new",{difficulty:Number($("difficulty").value),id:game?.id??null,revision:game?.revision??0});if(e!==epoch)return;selected=data.selected;accept(data);message("Нову головоломку створено.");}
 catch(e){disconnect(e)}finally{busy=false;render()}};
$("resume").onclick=()=>online?act("resume"):reconnect();
const buttons=[];for(let i=0;i<81;i++){let b=document.createElement("button");b.className="cell";b.onclick=()=>{selected=i;render();b.focus()};$("board").append(b);buttons.push(b)}
for(let d=1;d<=9;d++){const b=document.createElement("button");b.textContent=d;b.onclick=()=>enter(d);$("pad").append(b)}
function enter(d,temp=false){act("enter",{cell:selected,digit:d,note:temp||note})}
$("notes").onclick=()=>{note=!note;render()};
document.querySelectorAll("[data-action]").forEach(b=>b.onclick=()=>b.dataset.action==="erase"?enter(0):act(b.dataset.action));
function fmt(s){return Math.floor(s/60).toString().padStart(2,"0")+":"+String(s%60).padStart(2,"0")}
function render(){
 const active=online&&game&&!game.paused&&!game.completed&&!busy;
 $("cover").hidden=!!(online&&game&&!game.paused);$("covertext").textContent=!online?"Немає з’єднання":game?.paused?"Гра на паузі":"Створіть головоломку";
 $("resume").hidden=online&&!game;$("resume").textContent=online?"Продовжити":"Відновити з’єднання";
 $("newgame").disabled=!online||busy;$("level").textContent=game?levels[game.difficulty]:"–";$("time").textContent=fmt(game?.seconds||0);$("errors").textContent=game?.mistakes||0;
 $("progress").textContent=game?(game.completed?"✓ Судоку розв’язано! ":"")+game.values.filter(Boolean).length+"/81 · Підказки: "+game.hints:"";
 buttons.forEach((b,i)=>{const v=game?.values[i]||0,r=Math.floor(i/9),c=i%9,sr=Math.floor(selected/9),sc=selected%9;
 b.className="cell"+(c%3===2&&c!==8?" blockright":"")+(r%3===2&&r!==8?" blockbottom":"")+(game?.givens[i]?" given":"")+(i===selected?" selected":v&&v===game?.values[selected]?" same":r===sr||c===sc||Math.floor(r/3)===Math.floor(sr/3)&&Math.floor(c/3)===Math.floor(sc/3)?" peer":"");
 b.replaceChildren();if(v)b.textContent=v;else if(game?.notes[i]){let n=document.createElement("span");n.className="mininotes";for(let d=1;d<=9;d++){let k=document.createElement("span");k.textContent=game.notes[i]&(1<<d)?d:"";n.append(k)}b.append(n)}
 b.tabIndex=i===selected?0:-1;b.disabled=!online||!game||game.paused;b.setAttribute("aria-label","Рядок "+(r+1)+", стовпець "+(c+1)+": "+(v||"порожньо"));});
 document.querySelectorAll("#pad button,#tools button").forEach(b=>b.disabled=!active);
 document.querySelector('[data-action="undo"]').disabled=!active||!game?.canUndo;document.querySelector('[data-action="redo"]').disabled=!active||!game?.canRedo;$("notes").classList.toggle("active",note);
}
document.addEventListener("keydown",e=>{if(tab!=="play"||!online||!game||e.target.matches("input,select")||$("confirm").open)return;
 let k=e.key.toLowerCase();if(k==="p"||k==="escape"){act(game.paused?"resume":"pause");e.preventDefault();return}if(game.paused||game.completed)return;
 if(e.ctrlKey&&k==="z")act(e.shiftKey?"redo":"undo");else if(e.ctrlKey&&k==="y")act("redo");else if(e.ctrlKey)return;
 else if(/^[1-9]$/.test(k))enter(Number(k),e.shiftKey);else if(["delete","backspace","0"].includes(k))enter(0);else if(k==="n"||k===" "){note=!note;render()}else if(k==="h")act("hint");else if(k==="a")act("candidates");
 else if(k.startsWith("arrow")){let r=Math.floor(selected/9),c=selected%9;if(k==="arrowleft")c--;if(k==="arrowright")c++;if(k==="arrowup")r--;if(k==="arrowdown")r++;selected=Math.max(0,Math.min(8,r))*9+Math.max(0,Math.min(8,c));render();buttons[selected].focus()}else return;e.preventDefault()});
function row(parent,values){let r=document.createElement("div");r.className="record";values.forEach(v=>{let s=document.createElement("span");s.textContent=v;r.append(s)});parent.append(r)}
async function navigate(t){tab=t;["play","history","ranking"].forEach(n=>$(n).hidden=n!==t);try{
 if(t==="history"){let records=await api("history"),drafts=await api("desktop/drafts"),target=$("historyrows"),wins=records.filter(r=>r.outcome==="completed");target.replaceChildren();$("summary").textContent="Ігор: "+records.length+" · Перемог: "+wins.length+" · Балів: "+wins.reduce((s,r)=>s+r.score,0)+" · Час: "+fmt(records.reduce((s,r)=>s+r.game.seconds,0));row(target,["Дата / джерело","Складність","Результат / час","Оцінка"]);if(game&&!game.completed)row(target,["Поточна · web",levels[game.difficulty],"У процесі · "+fmt(game.seconds),"–"]);drafts.forEach(r=>row(target,[new Date(r.updated).toLocaleString("uk-UA")+" · desktop",levels[r.game.puzzle.difficulty],"У процесі · "+fmt(r.game.seconds),"–"]));records.forEach(r=>row(target,[new Date(r.ended).toLocaleString("uk-UA")+" · "+r.source,levels[r.game.puzzle.difficulty],(r.outcome==="completed"?"Перемога":"Перервано")+" · "+fmt(r.game.seconds),r.score+" балів · помилки: "+r.game.mistakes+" · підказки: "+r.game.hints]));}
 if(t==="ranking"){let ranks=await api("ranking?mode="+$("rankmode").value),target=$("rankrows");target.replaceChildren();row(target,["Місце","Гравець","Перемоги","Бали"]);ranks.forEach((r,i)=>row(target,[i+1,r.user,r.games,r.score]));}
 }catch(e){message(e.message)}}
$("rankmode").onchange=()=>navigate("ranking");
document.querySelectorAll("[data-tab]").forEach(b=>b.onclick=()=>navigate(b.dataset.tab));
render();if(token)start();else showAuth();
