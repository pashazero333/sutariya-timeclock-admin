const REPORT_API=SUPABASE_URL+'/functions/v1/timeclock-report-api';

function reportDateYmd(d){
  const z=n=>String(n).padStart(2,'0');
  return `${d.getFullYear()}-${z(d.getMonth()+1)}-${z(d.getDate())}`;
}

async function reportApi(path,body,method='GET'){
  return authFetch(REPORT_API,path,body,method);
}

async function refreshReportRecipientLine(){
  const line=document.getElementById('reportRecipientLine');
  if(!line)return;
  try{
    const {data,error}=await sb.from('app_settings').select('key,value').in('key',['payroll_email_recipient','payroll_email_additional']);
    if(error)throw error;
    const m=Object.fromEntries((data||[]).map(x=>[x.key,x.value]));
    const primary=String(m.payroll_email_recipient||'').trim();
    const additional=String(m.payroll_email_additional||'').trim();
    const all=[primary,additional].filter(Boolean).join(', ');
    line.textContent=all?`Report recipient: ${all}`:'No payroll report recipient configured.';
  }catch(e){
    line.textContent='Unable to load report recipient.';
  }
}

async function refreshEmailServiceStatus(){
  const status=document.getElementById('emailServiceStatus');
  const sender=document.getElementById('emailSenderLine');
  const testBtn=document.getElementById('sendTestEmailBtn');
  if(!status)return;
  status.textContent='Checking...';
  status.className='pill amber';
  try{
    const j=await reportApi('/api/payroll/status',undefined,'GET');
    if(j.email_connected){
      status.textContent='Connected';
      status.className='pill green';
      if(sender)sender.textContent=j.sender_email?`Sender: ${j.sender_email}`:'Sender is configured.';
      if(testBtn)testBtn.disabled=false;
    }else{
      status.textContent='Not connected';
      status.className='pill amber';
      if(sender)sender.textContent='Connect Gmail to enable Send Report Now and automatic delivery.';
      if(testBtn)testBtn.disabled=true;
    }
  }catch(e){
    status.textContent='Unable to check';
    status.className='pill red';
    if(sender)sender.textContent=String(e.message||e);
    if(testBtn)testBtn.disabled=true;
  }
}

async function generatePayrollReportNow(){
  const btn=document.getElementById('sendReportNowBtn');
  const msg=document.getElementById('sendReportMsg');
  if(!btn||!msg)return;
  const range=typeof getTimesheetRange==='function'?getTimesheetRange():null;
  if(!range||range.end<range.start){msg.textContent='Choose a valid date range first.';return}
  btn.disabled=true;
  btn.textContent='Sending...';
  msg.textContent='';
  try{
    const j=await reportApi('/api/payroll/report',{start:reportDateYmd(range.start),end:reportDateYmd(range.end)},'POST');
    msg.textContent=j.detail||(j.delivery_ready?'Report queued for Gmail delivery.':'Report generated.');
  }catch(e){
    msg.textContent='Error: '+String(e.message||e);
  }finally{
    btn.disabled=false;
    btn.textContent='Send Report Now';
  }
}

async function sendTestPayrollEmail(){
  const btn=document.getElementById('sendTestEmailBtn');
  const msg=document.getElementById('emailServiceMsg');
  if(!btn||!msg)return;
  btn.disabled=true;
  btn.textContent='Sending...';
  msg.textContent='';
  try{
    const j=await reportApi('/api/payroll/test',{},'POST');
    msg.textContent=j.detail||'Test email queued.';
  }catch(e){
    msg.textContent='Error: '+String(e.message||e);
  }finally{
    btn.disabled=false;
    btn.textContent='Send Test Email';
  }
}

(function initPayrollReportControls(){
  const timesheets=document.getElementById('page-payroll');
  const exportBtn=document.getElementById('exportCsvBtn');
  if(timesheets&&exportBtn&&!document.getElementById('sendReportNowBtn')){
    const btn=document.createElement('button');
    btn.id='sendReportNowBtn';
    btn.type='button';
    btn.textContent='Send Report Now';
    exportBtn.parentNode.insertBefore(btn,exportBtn);
    btn.addEventListener('click',generatePayrollReportNow);

    const filterCard=document.querySelector('.timesheet-filter-card');
    if(filterCard){
      const info=document.createElement('div');
      info.style.margin='0 0 18px';
      info.innerHTML='<span id="reportRecipientLine" class="muted">Loading report recipient...</span><span id="sendReportMsg" class="inline-msg" style="margin-left:14px"></span>';
      filterCard.insertAdjacentElement('afterend',info);
    }
  }

  const settings=document.getElementById('page-settings');
  if(settings&&!document.getElementById('emailServiceStatus')){
    const card=document.createElement('div');
    card.className='card';
    card.style.marginTop='18px';
    card.innerHTML='<h2 style="margin-top:0">Email Service</h2><p><span id="emailServiceStatus" class="pill amber">Checking...</span></p><p id="emailSenderLine" class="muted">Checking connection...</p><button id="sendTestEmailBtn" type="button" disabled>Send Test Email</button><span id="emailServiceMsg" class="inline-msg"></span>';
    settings.appendChild(card);
    document.getElementById('sendTestEmailBtn').addEventListener('click',sendTestPayrollEmail);
  }

  refreshReportRecipientLine();
  refreshEmailServiceStatus();
  document.getElementById('saveSettingsBtn')?.addEventListener('click',()=>setTimeout(()=>{refreshReportRecipientLine();refreshEmailServiceStatus()},350));
})();
