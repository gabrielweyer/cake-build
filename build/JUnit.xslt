<?xml version="1.0" encoding="UTF-8" ?>
<xsl:stylesheet version="2.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes" omit-xml-declaration="no" cdata-section-elements="message stack-trace"/>
  <xsl:template match="/">
    <testsuites>
      <xsl:for-each select="//assembly">
        <testsuite>
          <xsl:attribute name="name"><xsl:value-of select="@name"/></xsl:attribute>
          <xsl:attribute name="tests"><xsl:value-of select="@total"/></xsl:attribute>
          <xsl:for-each select="collection/test">
                <testcase>
                  <xsl:attribute name="name"><xsl:value-of select="@method"/></xsl:attribute>
                  <xsl:attribute name="classname"><xsl:value-of select="@type"/></xsl:attribute>
                </testcase>
          </xsl:for-each>
        </testsuite>
      </xsl:for-each>
    </testsuites>
  </xsl:template>
</xsl:stylesheet>