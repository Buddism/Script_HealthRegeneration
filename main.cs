//RequestPrefCategories
function HR_registerPref(%cat, %title, %type, %variable, %addon, %default, %params, %callback, %legacy, %isSecret, %isHostOnly) {
    new ScriptObject(Preference)
    {
        className     = "HR_preference";

        addon         = %addon;
        category      = %cat;
        title         = %title;

        type          = %type;
        params        = %params;

        variable      = %variable;

        defaultValue  = %default;

        hostOnly      = %isHostOnly;
        secret        = %isSecret;

        loadNow        = false; // load value on creation instead of with pool (optional)
        noSave         = false; // do not save (optional)
        requireRestart = false; // denotes a restart is required (optional)
    };
}
function HR_registerPrefs()
{
    //super hacky for debug
    // if(isObject(ScriptHealthRegenerationPrefs))
    //     ScriptHealthRegenerationPrefs.delete();
    //
    // for(%I = PreferenceGroup.getcount() - 1; %I >= 0; %I--)
    // {
    //     %t = PreferenceGroup.getObject(%I);
    //     if(%t.addon $= "Script_HealthRegeneration")
    //     {
    //         talk(%i);
    //         %t.delete();
    //     }
    // }

    //bit of a hack
    if(!isObject(ScriptHealthRegenerationPrefs))
    {
        registerPreferenceAddon("Script_HealthRegeneration", "Health Regeneration", "health");

        //general regen settings
        HR_registerPref("Health Regeneration", "Enabled"             , "dropdown" , "$Pref::HealthRegen::Enabled"          , "Script_HealthRegeneration", 2      , "Disabled 0 Enabled 1 MinigameOnly 2");
        HR_registerPref("Health Regeneration", "Regen Type"          , "dropdown" , "$Pref::HealthRegen::Type"             , "Script_HealthRegeneration", 0      , "Percentage 0 Additive 1");

        HR_registerPref("Health Regeneration", "Recover Time (MS)"   , "num"      , "$Pref::HealthRegen::RecoverTime"      , "Script_HealthRegeneration", 7500   , "0 30000 1");
        HR_registerPref("Health Regeneration", "Regen Amount"        , "num"      , "$Pref::HealthRegen::Amount"           , "Script_HealthRegeneration", 1      , "0 999999 2");

        HR_registerPref("Health Regeneration", "Damage Cancels Heal" , "bool"     , "$Pref::HealthRegen::DamageCancelsHeal", "Script_HealthRegeneration", 1      );
        HR_registerPref("Health Regeneration", "Start Regen on Kill" , "bool"     , "$Pref::HealthRegen::RegenOnKill"      , "Script_HealthRegeneration", false  );
        HR_registerPref("Health Regeneration", "Only Regen on Kill"  , "bool"     , "$Pref::HealthRegen::OnlyRegenOnKill"  , "Script_HealthRegeneration", false  );

        //vignette settings
        HR_registerPref("Vignette"           , "Heal Vignette Effect", "bool"     , "$Pref::HealthRegen::HealVignette"     , "Script_HealthRegeneration", false  );
        HR_registerPref("Vignette"           , "Vignette Strength"   , "num"      , "$Pref::HealthRegen::VignetteStrength" , "Script_HealthRegeneration", 0.4      , "0 1 5");
        HR_registerPref("Vignette"           , "Vignette Color"      , "rgb"      , "$Pref::HealthRegen::VignetteColor"    , "Script_HealthRegeneration", "1 1 1");
        HR_registerPref("Vignette"           , "Vignette Multiply"   , "bool"     , "$Pref::HealthRegen::vignetteMultiply" , "Script_HealthRegeneration", false);
    }
}
HR_registerPrefs();

function HR_isEnabled(%client)
{
    if($Pref::HealthRegen::Enabled == 1) //enabled
        return true;

    if($Pref::HealthRegen::Enabled == 2) //minigame only
        return isObject(%client.minigame);

    return false;
}
function HR_ApplyVignette(%obj, %client)
{
    if(!isObject(%obj) || !isObject(%client))
        return;

    %fadeIn = %obj.getDamagePercent() * $Pref::HealthRegen::VignetteStrength;
    %vignetteColor = removeWord(getColorF($Pref::HealthRegen::vignetteColor), 3);
    %vignetteMult = $Pref::HealthRegen::vignetteMultiply;

    commandToClient(%client, 'setVignette', %vignetteMult, %vignetteColor SPC %fadeIn);
}

package Script_HealthRegeneration
{
    function GameConnection::onDeath(%client, %sourceObject, %sourceClient, %damageType, %damLoc)
    {
        if(HR_isEnabled(%client) && $Pref::HealthRegen::HealVignette)
            EnvGuiServer::SendVignette(%client);

        if(HR_isEnabled(%sourceClient) && $Pref::HealthRegen::RegenOnKill)
            %sourceClient.player.HR_startRegen();

        parent::onDeath(%client, %sourceObject, %sourceClient, %damageType, %damLoc);
    }
    function Armor::onDamage(%this, %obj, %damage)
    {
        if($Pref::HealthRegen::DamageCancelsHeal)
        {
            //cancel health regen if taken damage
            cancel(%obj.HR_regenTick);
            cancel(%obj.HR_startRegen);
        }

        if(HR_isEnabled(%obj.client) && !$Pref::HealthRegen::OnlyRegenOnKill && %damage > 0.0 && %obj.getState() !$= "Dead")
        {
            %obj.HR_checkForRegen(%damage);

            %client = %obj.client;
            if($Pref::HealthRegen::HealVignette && isObject(%client))
                HR_ApplyVignette(%obj, %client);
        }

        parent::onDamage(%this, %obj, %damage);
    }
};
activatePackage(Script_HealthRegeneration);

function ShapeBase::HR_checkForRegen(%this, %damage)
{
    %recoverTime = $Pref::HealthRegen::RecoverTime;

    if(%recoverTime > 0)
    {
        cancel(%this.HR_startRegen);
        %this.schedule(%recoverTime, HR_startRegen);
    } else {
        %this.HR_startRegen();
    }
}

function ShapeBase::HR_startRegen(%this)
{
    if(!isEventPending(%this.HR_regenTick)) //if we are not currently healing
        %this.HR_regenTick();
}

function ShapeBase::addHealthPercent(%this, %percent)
{
    %healPercent = %this.getDatablock().maxDamage * (%percent / 100);
    %this.setDamageLevel(%this.getDamageLevel() - %healPercent);
}

function ShapeBase::HR_regenTick(%this)
{
    if(%this.getDamageLevel() == 0 || %this.getState() $= "Dead")
    {
        if($Pref::HealthRegen::HealVignette && isObject(%this.client))
            EnvGuiServer::SendVignette(%this.client);
        return;
    }

    switch($Pref::HealthRegen::Type)
    {
        case 0: //Percentage
            %this.addHealthPercent($Pref::HealthRegen::Amount);
        case 1: //Additive
            %this.addHealth($Pref::HealthRegen::Amount);
    }
    if($Pref::HealthRegen::HealVignette && isObject(%this.client))
        HR_ApplyVignette(%this, %this.client);

    %this.HR_regenTick = %this.schedule(32, HR_regenTick);
}
