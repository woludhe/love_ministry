ALTER TABLE MeetingRooms
  ADD COLUMN IsApproved       TINYINT(1)   DEFAULT 0          AFTER PassCode,
  ADD COLUMN ApprovalStatus   VARCHAR(20)  DEFAULT 'Pending'  AFTER IsApproved,
  ADD COLUMN ApprovedByUserId VARCHAR(450)                    AFTER ApprovalStatus,
  ADD COLUMN ApprovedAt       DATETIME                        AFTER ApprovedByUserId,
  ADD COLUMN RejectionReason  TEXT                            AFTER ApprovedAt;

SET SQL_SAFE_UPDATES = 0;

-- Backfill: meetings already created by admins are approved
-- (run this manually if you have existing data)
UPDATE MeetingRooms SET IsApproved=1, ApprovalStatus='Approved' WHERE HostUserId IN (
   SELECT UserId FROM AspNetUserRoles ur
   JOIN AspNetRoles r ON ur.RoleId=r.Id
   WHERE r.Name IN ('Admin','Pastor')
 );
 
 SET SQL_SAFE_UPDATES = 1;