"use strict";
const $=id=>document.getElementById(id), levels=["Легка","Середня","Складна","Експерт"];
let daily=null, dailyBusy=false, revealed=false;
let adPending=false, adClock=null, adKind="", adRemaining=8, adBusy=false, nextGuest=0;
let token=sessionStorage.getItem("token")||"", guestMode=sessionStorage.getItem("guest")==="1", registration=false, authBusy=false, game=null, selected=0, note=false, online=false, busy=false, tab="play", epoch=0;
const message=s=>$("status").textContent=s;
if(localStorage.getItem("theme")==="light")document.documentElement.classList.add("light");
$("theme").onclick=()=>{document.documentElement.classList.toggle("light");localStorage.setItem("theme",document.documentElement.classList.contains("light")?"light":"dark")};
async function api(path,body){
 const controller=new AbortController(), timeout=setTimeout(()=>controller.abort(),path==='web/new'?30000:5000);
 try{const r=await fetch("/api/"+path,{method:body===undefined?"GET":"POST",headers:{"Content-Type":"application/json","Authorization":"Bearer "+token},body:body===undefined?undefined:JSON.stringify(body),signal:controller.signal});
 if(r.status===401&&path!=="login"){token="";sessionStorage.removeItem("token");sessionStorage.removeItem("guest");resetSession();throw Error("Сеанс завершено. Відновлюємо демо.");}
 if(!r.ok){const problem=await r.json().catch(()=>({}));throw Object.assign(Error(problem.error||problem.detail||(r.status===409?"Сеанс змінено в іншій вкладці.":r.status===429?"Забагато спроб. Спробуйте через хвилину.":"Сервіс тимчасово недоступний.")),{status:r.status});}
 return await r.json().catch(()=>null);
 }finally{clearTimeout(timeout)}
}
function resetSession(){daily=null;revealed=false;if($("daily").open)$("daily").close();epoch++;game=null;online=false;guestMode=true;selected=0;note=false;adPending=false;if($("ad").open)$("ad").close();$("account").hidden=false;$("logout").hidden=true;$("history-tab").hidden=true;$("guest-notice").hidden=false;render()}
function showAuth(){mode(false);authError("");if(!$("auth").open)$("auth").showModal()}
$("account").onclick=showAuth;$("auth-close").onclick=()=>{$("auth").close();$("password").value=$("confirmation").value=""};
$("auth").addEventListener("close",()=>{$("password").value=$("confirmation").value=""});
function authError(text){$("auth-error").textContent=text;$("auth-error").hidden=!text}
function mode(register){registration=register;$("auth-title").textContent=register?"Створення акаунта":"Вхід до акаунта";$("confirmation-wrap").hidden=!register;$("confirmation").required=register;$("confirmation").value="";$("password").autocomplete=register?"new-password":"current-password";$("submit-auth").textContent=register?"Зареєструватися":"Увійти";$("register-tab").classList.toggle("active",register);$("login-tab").classList.toggle("active",!register);authError("")}
$("login-tab").onclick=()=>mode(false);$("register-tab").onclick=()=>mode(true);
document.querySelectorAll("[data-secret]").forEach(b=>b.onclick=()=>{const input=$(b.dataset.secret),show=input.type==="password";input.type=show?"text":"password";b.textContent=show?"Приховати":"Показати";b.setAttribute("aria-pressed",String(show));b.setAttribute("aria-label",b.textContent+" пароль")});
async function login(guest=false){
 if(authBusy||busy)return;authError("");if(guest)nextGuest=Date.now()+15000;
 if(!guest&&registration&&$("password").value!==$("confirmation").value){authError("Паролі не збігаються.");$("confirmation").focus();return}
 authBusy=true;$("submit-auth").disabled=$("guest").disabled=$("login-tab").disabled=$("register-tab").disabled=true;
 try{if(guest&&token&&guestMode){if($("auth").open)$("auth").close();return}
 const oldToken=token, oldGuest=guestMode;
 const a=await api(guest?"guest":registration?"register":"login",guest?{}:{user:$("user").value,password:$("password").value,confirmation:registration?$("confirmation").value:null});
 daily=null;epoch++;game=null;selected=0;note=false;token=a.token;guestMode=guest;sessionStorage.setItem("token",token);sessionStorage.setItem("guest",guest?"1":"0");$("password").value=$("confirmation").value="";if($("auth").open)$("auth").close();
 if(oldGuest&&oldToken&&oldToken!==token)fetch("/api/logout",{method:"POST",headers:{Authorization:"Bearer "+oldToken}}).catch(()=>{});
 await start();if(guest&&online&&!game)await $("newgame").onclick();}
 catch(e){const error=e.name==="AbortError"||e instanceof TypeError?"Не вдалося з’єднатися з сервісом. Спробуйте ще раз.":e.message;if(guest)message(error);else authError(error)}
 finally{authBusy=false;$("submit-auth").disabled=$("guest").disabled=$("login-tab").disabled=$("register-tab").disabled=false}
}
$("login").onsubmit=e=>{e.preventDefault();login()};$("guest").onclick=()=>login(true);
$("logout").onclick=async()=>{if(busy||authBusy)return;try{if(game&&!game.paused&&!game.completed)await act("pause");await api("logout",{})}catch{}token="";sessionStorage.removeItem("token");sessionStorage.removeItem("guest");resetSession();message("");await login(true)};
async function start(){$("app").hidden=false;$("account").hidden=!guestMode;$("logout").hidden=guestMode;$("guest-notice").hidden=!guestMode;$("history-tab").hidden=guestMode;await navigate("play");await reconnect();if(!guestMode)refreshDaily()}
function accept(data){game=data?.empty?null:data;online=navigator.onLine;if(game)selected=Math.max(0,Math.min(80,selected));render()}
async function reconnect(){
 if(busy||!token)return;const e=epoch;busy=true;
 try{const data=await api("web/session");if(e!==epoch)return;accept(data);if(game?.reward&&!$("ad").open){const cleared=await api("web/action",{id:game.id,revision:game.revision,action:"reward-cancel"});if(e!==epoch)return;accept(cleared);}if(game&&!game.paused&&!game.completed&&!game.outOfLives){const paused=await api("web/action",{id:game.id,revision:game.revision,action:"pause"});if(e!==epoch)return;accept(paused);}message(game?"З’єднання встановлено. Продовжіть гру.":"З’єднання встановлено. Оберіть складність.");}
 catch(e){if(token)disconnect(e);else authError(e.message)}finally{busy=false;render()}
}
function disconnect(e){online=false;message("Гру призупинено: "+e.message+". Очікуємо з’єднання.");render()}
window.addEventListener("offline",()=>disconnect(Error("немає мережі")));
window.addEventListener("online",()=>token?reconnect():login(true));
async function act(action,extra={}){
 if(!online||busy||!game)return false;const e=epoch;busy=true;
 try{const data=await api("web/action",{id:game.id,revision:game.revision,action,...extra});if(e!==epoch)return;accept(data);if(action==="hint")selected=game.selected;if(game.completed&&!guestMode)refreshDaily();return true;}
 catch(e){if(e.status===400)message(e.message);else disconnect(e);return false}finally{busy=false;render()}
}
setInterval(()=>{if(busy||authBusy||adBusy)return;if(!token){if(navigator.onLine&&!$("auth").open&&Date.now()>=nextGuest)login(true);return}if(!online)reconnect();else if(game)act("heartbeat")},3000);
setInterval(()=>{if(token&&online&&tab==="ranking")navigate("ranking")},10000);
function confirmNew(){return new Promise(resolve=>{const d=$("confirm");d.querySelector("p").textContent=guestMode?"Поточну демонстраційну гру буде замінено.":"Поточна незавершена гра залишиться в історії.";d.returnValue="cancel";d.addEventListener("close",()=>resolve(d.returnValue==="yes"),{once:true});d.showModal()})}
$("newgame").onclick=async()=>{if(!online||busy||adPending)return;if(game&&!game.completed&&!(await confirmNew()))return;if(!online||busy)return;const e=epoch;busy=true;render();message("Генерація головоломки…");
 try{const data=await api("web/new",{difficulty:Number($("difficulty").value),id:game?.id??null,revision:game?.revision??0});if(e!==epoch)return;selected=data.selected;accept(data);message("Нову головоломку створено.");}
 catch(e){disconnect(e)}finally{busy=false;render()}};
$("resume").onclick=()=>!online?(token?reconnect():login(true)):game?.outOfLives?chooseResource("life"):act("resume");
const buttons=[];for(let i=0;i<81;i++){let b=document.createElement("button");b.className="cell";b.onclick=()=>{selected=i;render();b.focus()};$("board").append(b);buttons.push(b)}
for(let d=1;d<=9;d++){const b=document.createElement("button");b.textContent=d;b.onclick=()=>enter(d);$("pad").append(b)}
function enter(d,temp=false){act("enter",{cell:selected,digit:d,note:temp||note})}
$("notes").onclick=()=>{note=!note;render()};
function tool(action){if(action==="hint"&&game?.hintCredits===0)return chooseResource("hint");if(action==="candidates"&&game?.candidateCredits===0)return chooseResource("candidates");return act(action)}
document.querySelectorAll("[data-action]").forEach(b=>b.onclick=()=>b.dataset.action==="erase"?enter(0):tool(b.dataset.action));
const rewardNames={life:"Одне життя",hint:"Одна підказка",candidates:"Одне використання кандидатів"};
const resourceNames={life:"Життя",hint:"Підказки",candidates:"Кандидати"};
const balance=k=>daily?.[k==="life"?"lives":k==="hint"?"hints":"candidates"]||0;
async function refreshDaily(){
 if(guestMode||!token||dailyBusy||!online)return;
 const e=epoch;try{const d=await api("daily");if(e!==epoch)return;daily=d;render();if($("daily").open&&!revealed)drawDaily()}catch{}
}
async function chooseResource(kind){
 if(!guestMode){await refreshDaily();if(balance(kind)>0){await spend(kind);return}}
 await reward(kind)
}
async function spend(kind){
 if(busy||dailyBusy||!game||!online||guestMode)return;
 if(await act("wallet",{reward:kind})){message("Використано ресурс зі сховища.");await refreshDaily()}
}
function drawDaily(){
 $("daily-summary").textContent=daily?"Сьогодні: "+daily.wins+"/7 перемог · доступно карток: "+daily.packs.length:"Підключення…";
 const wallet=$("wallet"),cards=$("daily-cards");wallet.replaceChildren();cards.replaceChildren();
 if(!daily)return;
 for(const kind of Object.keys(resourceNames)){const b=document.createElement("button");b.textContent=resourceNames[kind]+": "+balance(kind)+" · використати";const count=kind==="life"?game?.lives:kind==="hint"?game?.hintCredits:game?.candidateCredits;b.disabled=!game||game.completed||count>=3||!balance(kind)||dailyBusy;b.onclick=async()=>{b.disabled=true;await spend(kind);drawDaily()};wallet.append(b)}
 const pack=daily.packs[0];$("daily-milestone").textContent=!pack?"Усі доступні нагороди отримано.":({login:"Щоденний вхід",win1:"Перша перемога",win3:"Третя перемога",win7:"Сьома перемога · "+(pack.slot+1)+"/3"})[pack.milestone]+" · "+pack.day;
 if(!pack)return;
 for(let i=0;i<3;i++){const b=document.createElement("button");b.className="mystery-card";b.textContent="?";b.setAttribute("aria-label","Вибрати картку "+(i+1));b.disabled=dailyBusy;b.onclick=()=>chooseCard(pack,i);cards.append(b)}
}
async function chooseCard(pack,card){
 if(dailyBusy)return;dailyBusy=true;drawDaily();
 try{const r=await api("daily/choose",{...pack,card});daily=r.status;revealed=true;const cards=$("daily-cards");Array.from(cards.children).forEach((b,i)=>{b.textContent=i===card?resourceNames[r.item.kind]+" ×"+r.item.amount:"?";b.classList.toggle("revealed",i===card);b.disabled=true});$("daily-result").textContent="Отримано: "+resourceNames[r.item.kind]+" ×"+r.item.amount;const next=document.createElement("button");next.className="primary next-card";next.textContent="Далі";next.onclick=()=>{revealed=false;drawDaily()};cards.append(next);}
 catch(e){$("daily-result").textContent=e.message}
 finally{dailyBusy=false;if(!revealed)drawDaily();render()}
}
$("daily-open").onclick=async()=>{if(guestMode){showAuth();return}revealed=false;$("daily-result").textContent="";$("daily").showModal();drawDaily();await refreshDaily()};
$("daily-close").onclick=()=>$("daily").close();
setInterval(refreshDaily,15000);
function adRender(){$("ad-time").textContent=adRemaining>0?adRemaining+" с":"Готово!";$("ad-progress").value=Math.max(0,8-adRemaining);$("ad-claim").disabled=adRemaining>0||adBusy||busy||!online}
async function reward(kind){
 if(!online||busy||adPending||!game||game.completed)return;adPending=true;
 if(game.reward&&game.reward.kind!==kind){if(!await act("reward-cancel")){adPending=false;return}}
 if(!await act("reward-start",{reward:kind})){adPending=false;render();return}
 adKind=kind;adRemaining=game.reward?.remaining??8;$("ad-kind").textContent=rewardNames[kind];
 const deadline=performance.now()+adRemaining*1000;adRender();$("ad").showModal();
 adClock=setInterval(()=>{adRemaining=Math.max(0,Math.ceil((deadline-performance.now())/1000));adRender()},100);
}
$("ad-claim").onclick=async()=>{if(adRemaining>0||adBusy||busy)return;adBusy=true;adRender();const ok=await act("reward-claim",{reward:adKind});adBusy=false;if(ok){message("Винагороду отримано.");$("ad").close()}else adRender()};
$("ad-cancel").onclick=()=>$("ad").close();
$("ad").addEventListener("cancel",e=>{if(adBusy)e.preventDefault()});
$("ad").addEventListener("close",async()=>{clearInterval(adClock);if(game?.reward&&online){while(busy&&online)await new Promise(resolve=>setTimeout(resolve,50));if(online&&game?.reward)await act("reward-cancel")}adPending=false;render()});
document.querySelectorAll("[data-reward]").forEach(b=>b.onclick=()=>reward(b.dataset.reward));
function fmt(s){return Math.floor(s/60).toString().padStart(2,"0")+":"+String(s%60).padStart(2,"0")}
function render(){
 const active=online&&game&&!game.paused&&!game.completed&&!game.outOfLives&&!busy&&!adPending;
 $("cover").hidden=!!(online&&game&&!game.paused&&!game.outOfLives);$("covertext").textContent=!online?"Підключення до гри…":game?.outOfLives?"Життя закінчилися":game?.paused?"Гра на паузі":busy?"Генеруємо поле…":"Створіть головоломку";
 $("resume").hidden=online&&!game;$("resume").textContent=!online?"Відновити з’єднання":game?.outOfLives?"+1 життя · 8 с":"Продовжити";
 $("newgame").disabled=!online||busy;$("level").textContent=game?levels[game.difficulty]:"–";$("time").textContent=fmt(game?.seconds||0);$("lives").textContent=Array.from({length:3},(_,i)=>i<(game?.lives||0)?"♥":"♡").join(" ");$("score").textContent=(game?.currentScore||0).toLocaleString("uk-UA");$("penalties").textContent=game?"Час −"+Math.floor(game.seconds/3)+" · Помилки −"+75*game.mistakes+" · Підказки −"+150*game.hints+" · Кандидати −"+game.effectiveCandidatePenalty+" · Життя −"+100*game.lifeRestores:"";$("quest").textContent=guestMode?"Щоденні нагороди доступні в акаунті.":daily?"Сьогодні: "+daily.wins+"/7 · карток: "+daily.packs.length:"Щоденні нагороди · онлайн"; $("hint").textContent="Підказка ("+(game?.hintCredits||0)+")";$("candidates").textContent="Кандидати ("+(game?.candidateCredits||0)+")";$("candidates").title=game?"Зміни: "+game.candidateDelta+" · −"+(2*game.candidateDelta)+" балів":"";document.querySelectorAll("[data-reward]").forEach(b=>{const count=b.dataset.reward==="life"?game?.lives:b.dataset.reward==="hint"?game?.hintCredits:game?.candidateCredits;b.disabled=!online||!game||game.completed||busy||adPending||count>=3});
 $("progress").textContent=game?(game.completed?"✓ Судоку розв’язано! ":"")+game.values.filter(Boolean).length+"/81 · Підказки: "+game.hints:"";
 buttons.forEach((b,i)=>{const v=game?.values[i]||0,r=Math.floor(i/9),c=i%9,sr=Math.floor(selected/9),sc=selected%9;
 b.className="cell"+(c%3===2&&c!==8?" blockright":"")+(r%3===2&&r!==8?" blockbottom":"")+(game?.givens[i]?" given":"")+(i===selected?" selected":v&&v===game?.values[selected]?" same":r===sr||c===sc||Math.floor(r/3)===Math.floor(sr/3)&&Math.floor(c/3)===Math.floor(sc/3)?" peer":"");
 b.replaceChildren();if(v)b.textContent=v;else if(game?.notes[i]){let n=document.createElement("span");n.className="mininotes";for(let d=1;d<=9;d++){let k=document.createElement("span");k.textContent=game.notes[i]&(1<<d)?d:"";n.append(k)}b.append(n)}
 b.tabIndex=i===selected?0:-1;b.disabled=!online||!game||game.paused||game.outOfLives||adPending;b.setAttribute("aria-label","Рядок "+(r+1)+", стовпець "+(c+1)+": "+(v||"порожньо"));});
 document.querySelectorAll("#pad button,#tools button").forEach(b=>b.disabled=!active);
 document.querySelector('[data-action="undo"]').disabled=!active||!game?.canUndo;document.querySelector('[data-action="redo"]').disabled=!active||!game?.canRedo;$("notes").classList.toggle("active",note);
}
document.addEventListener("keydown",e=>{if(tab!=="play"||!online||!game||e.target.matches("input,select")||$("confirm").open||$("auth").open||$("ad").open||$("daily").open||adPending)return;
 let k=e.key.toLowerCase();if(k==="p"||k==="escape"){act(game.paused?"resume":"pause");e.preventDefault();return}if(game.paused||game.completed||game.outOfLives)return;
 if(e.ctrlKey&&k==="z")act(e.shiftKey?"redo":"undo");else if(e.ctrlKey&&k==="y")act("redo");else if(e.ctrlKey)return;
 else if(/^[1-9]$/.test(k))enter(Number(k),e.shiftKey);else if(["delete","backspace","0"].includes(k))enter(0);else if(k==="n"||k===" "){note=!note;render()}else if(k==="h")tool("hint");else if(k==="a")tool("candidates");
 else if(k.startsWith("arrow")){let r=Math.floor(selected/9),c=selected%9;if(k==="arrowleft")c--;if(k==="arrowright")c++;if(k==="arrowup")r--;if(k==="arrowdown")r++;selected=Math.max(0,Math.min(8,r))*9+Math.max(0,Math.min(8,c));render();buttons[selected].focus()}else return;e.preventDefault()});
function row(parent,values){let r=document.createElement("div");r.className="record";values.forEach(v=>{let s=document.createElement("span");s.textContent=v;r.append(s)});parent.append(r)}
async function navigate(t){tab=t;["play","history","ranking"].forEach(n=>$(n).hidden=n!==t);try{
 if(t==="history"&&!guestMode){let records=await api("history"),drafts=await api("desktop/drafts"),target=$("historyrows"),wins=records.filter(r=>r.outcome==="completed");target.replaceChildren();$("summary").textContent="Ігор: "+records.length+" · Перемог: "+wins.length+" · Балів: "+wins.reduce((s,r)=>s+r.score,0)+" · Час: "+fmt(records.reduce((s,r)=>s+r.game.seconds,0));row(target,["Дата / джерело","Складність","Результат / час","Оцінка"]);if(game&&!game.completed)row(target,["Поточна · web",levels[game.difficulty],"У процесі · "+fmt(game.seconds),"–"]);drafts.forEach(r=>row(target,[new Date(r.updated).toLocaleString("uk-UA")+" · desktop",levels[r.game.puzzle.difficulty],"У процесі · "+fmt(r.game.seconds),"–"]));records.forEach(r=>row(target,[new Date(r.ended).toLocaleString("uk-UA")+" · "+r.source,levels[r.game.puzzle.difficulty],(r.outcome==="completed"?"Перемога":"Перервано")+" · "+fmt(r.game.seconds),r.score+" балів · помилки: "+r.game.mistakes+" · підказки: "+r.game.hints+" · кандидати: "+(r.game.candidateUses||0)+" · відновлення: "+(r.game.lifeRestores||0)]));}
 if(t==="ranking"){const mode=$("rankmode").value;let ranks=await api("ranking?mode="+mode),target=$("rankrows");if(tab!=="ranking"||$("rankmode").value!==mode)return;target.replaceChildren();row(target,["Місце","Гравець","Перемоги","Бали"]);ranks.forEach((r,i)=>row(target,[i+1,r.user,r.games,r.score]));}
 }catch(e){message(e.message)}}
$("rankmode").onchange=()=>navigate("ranking");
document.querySelectorAll("[data-tab]").forEach(b=>b.onclick=()=>navigate(b.dataset.tab));
render();if(token)start().then(()=>{if(guestMode&&online&&!game)$("newgame").onclick()});else {resetSession();login(true)}
