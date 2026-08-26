-- ── Live Session Tables ────────────────────────────────────
-- Run after your existing database_setup.sql
-- OR add to your EF Core migration

CREATE TABLE IF NOT EXISTS LiveSessions (
  Id                   INT AUTO_INCREMENT PRIMARY KEY,
  SermonId             INT NOT NULL UNIQUE,
  ShowVerseOverlay     TINYINT(1) DEFAULT 1,
  ActiveVerseId        INT,
  AutoRotateVerses     TINYINT(1) DEFAULT 0,
  VerseRotateSeconds   INT DEFAULT 60,
  ShowMarquee          TINYINT(1) DEFAULT 1,
  MarqueeSpeed         VARCHAR(20) DEFAULT 'Normal',
  ShowGivingPanel      TINYINT(1) DEFAULT 1,
  GivingCta            VARCHAR(300) DEFAULT 'Support this ministry - give today!',
  GivingPanelPosition  VARCHAR(20) DEFAULT 'Sidebar',
  ShowMtn              TINYINT(1) DEFAULT 1,
  ShowAirtel           TINYINT(1) DEFAULT 1,
  ShowMpesa            TINYINT(1) DEFAULT 1,
  ShowVisa             TINYINT(1) DEFAULT 0,
  ShowPrayerButton     TINYINT(1) DEFAULT 0,
  CreatedAt            DATETIME DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt            DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  FOREIGN KEY (SermonId) REFERENCES Sermons(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS LiveBibleVerses (
  Id            INT AUTO_INCREMENT PRIMARY KEY,
  LiveSessionId INT NOT NULL,
  Reference     VARCHAR(50)  NOT NULL,
  VerseText     TEXT         NOT NULL,
  Translation   VARCHAR(10)  DEFAULT 'KJV',
  IsActive      TINYINT(1)   DEFAULT 0,
  SortOrder     INT          DEFAULT 0,
  CreatedAt     DATETIME     DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (LiveSessionId) REFERENCES LiveSessions(Id) ON DELETE CASCADE
);

-- Add FK after LiveBibleVerses exists
ALTER TABLE LiveSessions
  ADD CONSTRAINT fk_live_active_verse
  FOREIGN KEY (ActiveVerseId) REFERENCES LiveBibleVerses(Id)
  ON DELETE SET NULL;

CREATE TABLE IF NOT EXISTS LiveAnnouncements (
  Id            INT AUTO_INCREMENT PRIMARY KEY,
  LiveSessionId INT NOT NULL,
  Text          TEXT         NOT NULL,
  IsActive      TINYINT(1)   DEFAULT 0,
  SortOrder     INT          DEFAULT 0,
  CreatedAt     DATETIME     DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (LiveSessionId) REFERENCES LiveSessions(Id) ON DELETE CASCADE
);

SELECT 'Live session tables created successfully.' AS Status;
