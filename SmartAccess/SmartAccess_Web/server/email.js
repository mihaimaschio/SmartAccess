const nodemailer = require('nodemailer');

function createTransporter() {
  if (!process.env.SMTP_USER || !process.env.SMTP_PASS) return null;
  return nodemailer.createTransport({
    service: 'gmail',
    auth: {
      user: process.env.SMTP_USER,
      pass: process.env.SMTP_PASS,
    },
  });
}

async function sendInviteEmail({ to, full_name, inviteUrl, invitedBy }) {
  const transporter = createTransporter();
  if (!transporter) {
    console.warn('Email not configured - skipping invite email (set SMTP_USER and SMTP_PASS in .env)');
    return false;
  }

  await transporter.sendMail({
    from: `"SmartAccess" <${process.env.SMTP_USER}>`,
    to,
    subject: 'You have been invited to SmartAccess',
    html: `
<!DOCTYPE html>
<html>
<body style="font-family:sans-serif; background:#f5f5f5; padding:32px;">
  <div style="max-width:480px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; border:1px solid #e0e0e0;">
    <div style="background:#0a0a0a; padding:28px 32px; text-align:center;">
      <h1 style="color:#fff; margin:0; font-size:20px; letter-spacing:3px;">SMART ACCESS</h1>
    </div>
    <div style="padding:32px;">
      <h2 style="margin:0 0 12px; font-size:18px; color:#0a0a0a;">Hi ${full_name},</h2>
      <p style="color:#555; line-height:1.6; margin:0 0 24px;">
        ${invitedBy} has invited you to join <strong>SmartAccess</strong>, the home access control system.
        Click the button below to set your password and activate your account.
      </p>
      <a href="${inviteUrl}" style="display:inline-block; background:#0a0a0a; color:#fff; text-decoration:none; padding:14px 32px; border-radius:8px; font-weight:700; font-size:14px; letter-spacing:1px;">
        ACTIVATE ACCOUNT
      </a>
      <p style="color:#999; font-size:12px; margin:24px 0 0; line-height:1.5;">
        This link expires in <strong>48 hours</strong>. If you were not expecting this invitation, you can ignore this email.
      </p>
      <p style="color:#ccc; font-size:11px; margin:16px 0 0; word-break:break-all;">
        ${inviteUrl}
      </p>
    </div>
  </div>
</body>
</html>`,
  });

  return true;
}

module.exports = { sendInviteEmail };
